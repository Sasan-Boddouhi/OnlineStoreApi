namespace Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public int QueueCapacity { get; set; } = 1000;
    public int EnqueueTimeoutSeconds { get; set; } = 5;
    public int MaxIdempotencyKeyLength { get; set; } = 256;
    public int RetryQueueCapacity { get; set; } = 100;
    public int RetryEnqueueTimeoutSeconds { get; set; } = 5;
    public int RetryBaseDelaySeconds { get; set; } = 1;
    public int RetryMaxDelaySeconds { get; set; } = 300;
    public int MaxExecutionAttempts { get; set; } = 5;
    public int MaxRequeueAttempts { get; set; } = 10;
    public int JobTimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        if (QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (EnqueueTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(EnqueueTimeoutSeconds));
        if (MaxIdempotencyKeyLength <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIdempotencyKeyLength));
        if (RetryQueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(RetryQueueCapacity));
        if (RetryEnqueueTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(RetryEnqueueTimeoutSeconds));
        if (RetryBaseDelaySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(RetryBaseDelaySeconds));
        if (RetryMaxDelaySeconds < RetryBaseDelaySeconds) throw new ArgumentOutOfRangeException(nameof(RetryMaxDelaySeconds));
        if (MaxExecutionAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxExecutionAttempts));
        if (MaxRequeueAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxRequeueAttempts));
        if (JobTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(JobTimeoutSeconds));
    }
}
