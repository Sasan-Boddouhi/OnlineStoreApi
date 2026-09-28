using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Application.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        _logger.LogInformation(
            "BackgroundJobStarted JobId={JobId} JobType={JobType} Attempt={Attempt} IdempotencyKey={IdempotencyKey} CorrelationId={CorrelationId} TraceId={TraceId}",
            context.JobId,
            context.JobType,
            context.Attempt,
            context.IdempotencyKey,
            context.CorrelationId,
            context.TraceId);

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
        }
        catch (TimeoutException)
        {
            executionCts.Cancel();

            _logger.LogWarning(
                "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                context.JobId,
                context.JobType,
                context.Attempt,
                JobCancellationReason.JobTimeout);

            throw;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                context.JobId,
                context.JobType,
                context.Attempt,
                JobCancellationReason.HostShutdown);
            throw;
        }
    }
}
