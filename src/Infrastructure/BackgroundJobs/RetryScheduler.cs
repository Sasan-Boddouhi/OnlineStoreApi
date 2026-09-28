using System.Threading.Channels;
using Application.BackgroundJobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

public sealed class RetryScheduler : BackgroundService
{
    private readonly Channel<RetryItem> _inbox;
    private readonly SemaphoreSlim _wakeSignal = new(0);
    private readonly ChannelBackgroundJobQueue _queue;
    private readonly BackgroundJobOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetryScheduler> _logger;
    private readonly PriorityQueue<RetryItem, DateTimeOffset> _pending = new();

    public RetryScheduler(
        ChannelBackgroundJobQueue queue,
        IOptions<BackgroundJobOptions> options,
        TimeProvider timeProvider,
        ILogger<RetryScheduler> logger)
    {
        _queue = queue;
        _options = options.Value;
        _options.Validate();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger;

        _inbox = Channel.CreateBounded<RetryItem>(
            new BoundedChannelOptions(_options.RetryQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public async ValueTask<bool> ScheduleAsync(
        BackgroundJobEnvelope envelope,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var item = new RetryItem(
            envelope,
            _timeProvider.GetUtcNow().Add(delay),
            RequeueAttempts: 0);

        try
        {
            var waitTask = _inbox.Writer.WaitToWriteAsync(cancellationToken).AsTask();

            if (!await waitTask.WaitAsync(
                    TimeSpan.FromSeconds(_options.RetryEnqueueTimeoutSeconds),
                    _timeProvider,
                    cancellationToken).ConfigureAwait(false))
                return false;

            if (!_inbox.Writer.TryWrite(item))
                return false;

            _wakeSignal.Release();
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (_inbox.Reader.TryRead(out var item))
                    _pending.Enqueue(item, item.DueAt);

                if (_pending.Count == 0)
                {
                    await _wakeSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var next = _pending.Peek();
                var now = _timeProvider.GetUtcNow();

                if (next.DueAt <= now)
                {
                    _pending.Dequeue();
                    await ProcessDueAsync(next, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var wait = next.DueAt - now;
                var delayTask = _timeProvider is TimeProvider.System
                    ? Task.Delay(wait, stoppingToken)
                    : Task.Delay(wait, _timeProvider, stoppingToken);
                var wakeTask = _wakeSignal.WaitAsync(stoppingToken);

                await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessDueAsync(
        RetryItem item,
        CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
            return;

        var status = await _queue.RequeueAsync(
            item.Envelope,
            stoppingToken).ConfigureAwait(false);

        if (status == EnqueueStatus.Enqueued)
        {
            _logger.LogInformation(
                "BackgroundJobRetryEnqueued JobId={JobId} JobType={JobType} Attempt={Attempt}",
                item.Envelope.JobId,
                item.Envelope.JobType,
                item.Envelope.Attempt);
            return;
        }

        if (status == EnqueueStatus.ShuttingDown)
        {
            _logger.LogInformation(
                "BackgroundJobCancelled JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                item.Envelope.JobId,
                item.Envelope.JobType,
                item.Envelope.Attempt,
                JobCancellationReason.HostShutdown);
            return;
        }

        var nextAttempt = item.RequeueAttempts + 1;

        if (nextAttempt >= _options.MaxRequeueAttempts)
        {
            _logger.LogWarning(
                "BackgroundJobRetryQueueStarvation JobId={JobId} JobType={JobType} Attempt={Attempt} CancellationReason={Reason}",
                item.Envelope.JobId,
                item.Envelope.JobType,
                item.Envelope.Attempt,
                JobCancellationReason.RetryQueueStarvation);
            return;
        }

        var retryDelay = GetRequeueDelay(nextAttempt);
        var rescheduled = item with
        {
            DueAt = _timeProvider.GetUtcNow().Add(retryDelay),
            RequeueAttempts = nextAttempt
        };

        _pending.Enqueue(rescheduled, rescheduled.DueAt);
    }

    private TimeSpan GetRequeueDelay(int requeueAttempt)
    {
        var exponent = Math.Min(requeueAttempt - 1, 30);
        var multiplier = Math.Pow(2, exponent);
        var seconds = Math.Min(
            _options.RetryBaseDelaySeconds * multiplier,
            _options.RetryMaxDelaySeconds);

        return TimeSpan.FromSeconds(seconds);
    }

    public override void Dispose()
    {
        _wakeSignal.Dispose();
        base.Dispose();
    }
}
