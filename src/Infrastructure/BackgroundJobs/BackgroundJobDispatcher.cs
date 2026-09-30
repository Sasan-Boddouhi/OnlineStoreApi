using Application.BackgroundJobs;
using Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Infrastructure.BackgroundJobs;

public sealed class BackgroundJobDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyDictionary<string, BackgroundJobHandlerRegistration> _registrations;
    private readonly TimeProvider _timeProvider;
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<BackgroundJobDispatcher> _logger;

    public BackgroundJobDispatcher(
        IServiceScopeFactory scopeFactory,
        IEnumerable<BackgroundJobHandlerRegistration> registrations,
        TimeProvider timeProvider,
        IOptions<BackgroundJobOptions> options,
        ILogger<BackgroundJobDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _registrations = registrations
            .GroupBy(x => x.JobTypeName)
            .ToDictionary(x => x.Key, x => x.First());
        _timeProvider = timeProvider;
        _options = options.Value;
        _options.Validate();
        _logger = logger;
    }

    public async Task DispatchAsync(
        BackgroundJobEnvelope envelope,
        CancellationToken stoppingToken)
    {
        if (!_registrations.TryGetValue(envelope.JobType, out var registration))
            throw new InvalidOperationException(
                $"No background job handler is registered for '{envelope.JobType}'.");

        using var scope = _scopeFactory.CreateScope();
        var handlerServiceType = typeof(IBackgroundJobHandler<>).MakeGenericType(registration.JobType);
        var handler = scope.ServiceProvider.GetRequiredService(handlerServiceType);

        var job = JsonSerializer.Deserialize(
            envelope.Payload,
            registration.JobType);

        if (job is null)
            throw new JsonException(
                $"Unable to deserialize background job payload for '{envelope.JobType}'.");

        var attempt = envelope.Attempt + 1;
        var context = new BackgroundJobExecutionContext(
            envelope.JobId,
            envelope.JobType,
            attempt,
            envelope.IdempotencyKey,
            envelope.CorrelationId,
            envelope.TraceId);

        using var executionCts =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var startedAt = _timeProvider.GetTimestamp();

        var parentContext = default(ActivityContext);

        if (!string.IsNullOrEmpty(envelope.TraceId) &&
            !string.IsNullOrEmpty(envelope.ParentSpanId) &&
            ActivityContext.TryParse(
                $"00-{envelope.TraceId}-{envelope.ParentSpanId}-01",
                traceState: null,
                out var parsed))
        {
            parentContext = parsed;
        }

        using var activity = BackgroundJobActivitySource.Instance
            .StartActivity(
                "background_job.execute",
                ActivityKind.Consumer,
                parentContext);

        activity?.SetTag("job.id", context.JobId);
        activity?.SetTag("job.type", context.JobType);
        activity?.SetTag("job.attempt", context.Attempt);
        activity?.SetTag("idempotency.key", context.IdempotencyKey);

        _logger.LogInformation(
            "BackgroundJobStarted JobId={JobId} JobType={JobType} Attempt={Attempt} IdempotencyKey={IdempotencyKey} CorrelationId={CorrelationId} TraceId={TraceId}",
            context.JobId,
            context.JobType,
            context.Attempt,
            context.IdempotencyKey,
            context.CorrelationId,
            context.TraceId);

        var outcome = "exception";
        JobCancellationReason? cancellationReason = null;

        try
        {
            var handleMethod = handlerServiceType.GetMethod(
                nameof(IBackgroundJobHandler<object>.HandleAsync),
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"Handler method was not found for '{envelope.JobType}'.");

            Task task;
            try
            {
                task = (Task?)handleMethod.Invoke(
                    handler,
                    new object?[] { job, context, executionCts.Token })
                    ?? throw new InvalidOperationException(
                        $"Handler returned no Task for '{envelope.JobType}'.");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            await task.WaitAsync(
                TimeSpan.FromSeconds(_options.JobTimeoutSeconds),
                _timeProvider,
                stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "BackgroundJobSucceeded JobId={JobId} JobType={JobType} Attempt={Attempt} DurationMs={DurationMs}",
                context.JobId,
                context.JobType,
                context.Attempt,
                _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);

            outcome = "succeeded";
        }
        catch (TimeoutException)
        {
            cancellationReason = JobCancellationReason.JobTimeout;
            outcome = "cancelled";

            executionCts.Cancel();

            _logger.LogWarning(
                "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                context.JobId,
                context.JobType,
                context.Attempt,
                cancellationReason.Value);

            throw;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            cancellationReason = JobCancellationReason.HostShutdown;
            outcome = "cancelled";

            _logger.LogInformation(
                "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                context.JobId,
                context.JobType,
                context.Attempt,
                cancellationReason.Value);

            throw;
        }
        finally
        {
            var durationSeconds = _timeProvider.GetElapsedTime(startedAt).TotalSeconds;

            OnlineStoreMetrics.BackgroundJobsExecutionDuration.Record(
                durationSeconds,
                new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("job.type", context.JobType));

            if (outcome == "succeeded")
            {
                OnlineStoreMetrics.BackgroundJobsCompleted.Add(
                    1,
                    new KeyValuePair<string, object?>("job.type", context.JobType));
            }
            else if (cancellationReason.HasValue)
            {
                OnlineStoreMetrics.BackgroundJobsCancelled.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", cancellationReason.Value.ToString()),
                    new KeyValuePair<string, object?>("job.type", context.JobType));
            }
        }
    }
}
