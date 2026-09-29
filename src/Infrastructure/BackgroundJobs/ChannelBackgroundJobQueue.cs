using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Application.BackgroundJobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

public sealed class ChannelBackgroundJobQueue :
    IBackgroundJobQueue,
    IBackgroundJobConsumer,
    IBackgroundJobRequeue
{
    private readonly Channel<BackgroundJobEnvelope> _channel;
    private readonly ChannelBackgroundJobQueueOptions _options;
    private readonly TimeProvider _timeProvider;
    private int _isShuttingDown;

    public ChannelBackgroundJobQueue(
        IOptions<ChannelBackgroundJobQueueOptions> options,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _options.Validate();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _channel = Channel.CreateBounded<BackgroundJobEnvelope>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        applicationLifetime.ApplicationStopping.Register(
            static state => ((ChannelBackgroundJobQueue)state!).BeginShutdown(),
            this);
    }

    public async ValueTask<EnqueueResult> EnqueueAsync<TJob>(
        TJob job,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);

        if (Volatile.Read(ref _isShuttingDown) != 0)
            return new EnqueueResult(EnqueueStatus.ShuttingDown);

        var envelope = CreateEnvelope(job, idempotencyKey);

        if (Volatile.Read(ref _isShuttingDown) != 0)
            return new EnqueueResult(EnqueueStatus.ShuttingDown);

        return await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<BackgroundJobEnvelope> DequeueAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    public async ValueTask<EnqueueStatus> RequeueAsync(
        BackgroundJobEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0)
            return EnqueueStatus.ShuttingDown;

        return (await WriteAsync(envelope, cancellationToken).ConfigureAwait(false)).Status;
    }

    private BackgroundJobEnvelope CreateEnvelope<TJob>(
        TJob job,
        string idempotencyKey)
    {
        var payload = JsonSerializer.SerializeToElement(job).Clone();
        var activity = Activity.Current;

        return new BackgroundJobEnvelope(
            Guid.NewGuid(),
            typeof(TJob).FullName ?? typeof(TJob).Name,
            payload,
            _timeProvider.GetUtcNow(),
            Attempt: 0,
            idempotencyKey,
            activity?.GetBaggageItem("correlation.id")
                ?? activity?.GetBaggageItem("correlationId"),
            activity?.TraceId.ToString());
    }

    private async ValueTask<EnqueueResult> WriteAsync(
        BackgroundJobEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0)
            return new EnqueueResult(EnqueueStatus.ShuttingDown);

        try
        {
            var waitTask = _channel.Writer.WaitToWriteAsync(cancellationToken).AsTask();

            if (!await waitTask.WaitAsync(
                    TimeSpan.FromSeconds(_options.EnqueueTimeoutSeconds),
                    _timeProvider,
                    cancellationToken).ConfigureAwait(false))
            {
                return new EnqueueResult(
                    Volatile.Read(ref _isShuttingDown) != 0
                        ? EnqueueStatus.ShuttingDown
                        : EnqueueStatus.QueueFull);
            }

            if (Volatile.Read(ref _isShuttingDown) != 0)
                return new EnqueueResult(EnqueueStatus.ShuttingDown);

            if (!_channel.Writer.TryWrite(envelope))
                return new EnqueueResult(
                    Volatile.Read(ref _isShuttingDown) != 0
                        ? EnqueueStatus.ShuttingDown
                        : EnqueueStatus.QueueFull);

            return new EnqueueResult(EnqueueStatus.Enqueued);
        }
        catch (TimeoutException)
        {
            return new EnqueueResult(
                Volatile.Read(ref _isShuttingDown) != 0
                    ? EnqueueStatus.ShuttingDown
                    : EnqueueStatus.QueueFull);
        }
        catch (ChannelClosedException) when (
            Volatile.Read(ref _isShuttingDown) != 0)
        {
            return new EnqueueResult(EnqueueStatus.ShuttingDown);
        }
    }

    private void ValidateIdempotencyKey(string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException(
                "Idempotency key cannot be empty or whitespace.",
                nameof(idempotencyKey));

        if (idempotencyKey.Length > _options.MaxIdempotencyKeyLength)
            throw new ArgumentOutOfRangeException(
                nameof(idempotencyKey),
                idempotencyKey.Length,
                $"Idempotency key cannot exceed {_options.MaxIdempotencyKeyLength} characters.");
    }

    private void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) == 0)
            _channel.Writer.TryComplete();
    }
}
