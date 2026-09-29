namespace Application.BackgroundJobs;

public enum EnqueueStatus
{
    Enqueued,
    QueueFull,
    ShuttingDown
}
