using System.Threading.Channels;
using Application.BackgroundJobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundJobEnvelope envelope;

            try
            {
                envelope = await _consumer.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            try
            {
                await _dispatcher.DispatchAsync(envelope, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
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
                            stoppingToken).ConfigureAwait(false))
                    {
                        _logger.LogWarning(
                            exception,
                            "BackgroundJobRetryScheduled JobId={JobId} JobType={JobType} Attempt={Attempt} DelaySeconds={DelaySeconds}",
                            envelope.JobId,
                            envelope.JobType,
                            attempt,
                            delay.TotalSeconds);
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
                    }

                    continue;
                }

                _logger.LogError(
                    exception,
                    "BackgroundJobFailed JobId={JobId} JobType={JobType} Attempt={Attempt}",
                    envelope.JobId,
                    envelope.JobType,
                    attempt);
            }
        }
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
