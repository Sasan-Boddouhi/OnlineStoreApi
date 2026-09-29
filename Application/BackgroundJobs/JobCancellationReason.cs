namespace Application.BackgroundJobs;

public enum JobCancellationReason
{
    HostShutdown,
    RetryQueueStarvation,
    RetrySchedulerStarvation,
    JobTimeout
}
