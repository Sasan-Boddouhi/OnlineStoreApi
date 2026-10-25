namespace Application.BackgroundJobs;

public readonly record struct EnqueueResult(EnqueueStatus Status)
{
    public bool IsAccepted => Status == EnqueueStatus.Enqueued;
}
