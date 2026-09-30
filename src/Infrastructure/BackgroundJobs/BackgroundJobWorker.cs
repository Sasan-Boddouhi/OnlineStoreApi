using System.Threading.Channels;
using Application.BackgroundJobs;
using Application.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

namespace Infrastructure.BackgroundJobs;

public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobConsumer _consumer;
    private readonly BackgroundJobDispatcher _dispatcher;
    private readonly RetryScheduler _retryScheduler;
    private readonly IRetryPolicy _retryPolicy;
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<BackgroundJobWorker> _logger;

    public BackgroundJobWorker(
        IBackgroundJobConsumer consumer,
        BackgroundJobDispatcher dispatcher,
        RetryScheduler retryScheduler,
        IRetryPolicy retryPolicy,
        IOptions<BackgroundJobOptions> options,
        ILogger<BackgroundJobWorker> logger)
    {
        _consumer = consumer;
        _dispatcher = dispatcher;
        _retryScheduler = retryScheduler;
        _retryPolicy = retryPolicy;
        _options = options.Value;
        _options.Validate();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            BackgroundJobEnvelope envelope;

            try
            {
                // Queue shutdown completes the writer and lets accepted work drain.
                envelope = await _consumer.DequeueAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                break;
            }

            try
            {
                // Host shutdown does not cancel an accepted execution; graceful drain
                // is bounded by the host's StopAsync cancellation token.
                await _dispatcher.DispatchAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var attempt = envelope.Attempt + 1;
                var context = new BackgroundJobExecutionContext(
                    envelope.JobId,
                    envelope.JobType,
                    attempt,
                    envelope.IdempotencyKey,
                    envelope.CorrelationId,
                    envelope.TraceId);

                var decision = _retryPolicy.Evaluate(exception, context);

                if (decision == RetryDecision.Retry)
                {
                    var delay = GetRetryDelay(attempt);
                    var retryEnvelope = envelope with { Attempt = attempt };

                    if (await _retryScheduler.ScheduleAsync(
                            retryEnvelope,
                            delay,
                            CancellationToken.None).ConfigureAwait(false))
                    {
                        _logger.LogWarning(
                            exception,
                            "BackgroundJobRetryScheduled JobId={JobId} JobType={JobType} Attempt={Attempt} DelaySeconds={DelaySeconds}",
                            envelope.JobId,
                            envelope.JobType,
                            attempt,
                            delay.TotalSeconds);

                        OnlineStoreMetrics.BackgroundJobsRetryDelay.Record(delay.TotalSeconds);

                        OnlineStoreMetrics.BackgroundJobsRetried.Add(
                            1,
                            new KeyValuePair<string, object?>("job.type", envelope.JobType));
                    }
                    else
                    {
                        _logger.LogWarning(
                            exception,
                            "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                            envelope.JobId,
                            envelope.JobType,
                            attempt,
                            JobCancellationReason.RetrySchedulerStarvation);

                        OnlineStoreMetrics.BackgroundJobsCancelled.Add(
                            1,
                            new KeyValuePair<string, object?>("reason", JobCancellationReason.RetrySchedulerStarvation.ToString()),
                            new KeyValuePair<string, object?>("job.type", envelope.JobType));
                    }

                    continue;
                }

                _logger.LogError(
                    exception,
                    "BackgroundJobFailed JobId={JobId} JobType={JobType} Attempt={Attempt}",
                    envelope.JobId,
                    envelope.JobType,
                    attempt);

                OnlineStoreMetrics.BackgroundJobsFailed.Add(
                    1,
                    new KeyValuePair<string, object?>("job.type", envelope.JobType));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var executeTask = ExecuteTask;
        if (executeTask is null)
            return;

        await Task.WhenAny(
            executeTask,
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var exponent = Math.Min(attempt - 1, 30);
        var seconds = Math.Min(
            _options.RetryBaseDelaySeconds * Math.Pow(2, exponent),
            _options.RetryMaxDelaySeconds);

        return TimeSpan.FromSeconds(seconds);
    }
}
