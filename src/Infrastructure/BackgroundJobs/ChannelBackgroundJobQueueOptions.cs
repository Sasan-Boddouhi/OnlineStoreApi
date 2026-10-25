namespace Infrastructure.BackgroundJobs;

public sealed class ChannelBackgroundJobQueueOptions
{
    public int QueueCapacity { get; set; } = 1000;
    public int EnqueueTimeoutSeconds { get; set; } = 5;
    public int MaxIdempotencyKeyLength { get; set; } = 256;

    internal void Validate()
    {
        if (QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity));

        if (EnqueueTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(EnqueueTimeoutSeconds));

        if (MaxIdempotencyKeyLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxIdempotencyKeyLength));
    }
}
