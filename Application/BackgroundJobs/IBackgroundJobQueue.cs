namespace Application.BackgroundJobs;

public interface IBackgroundJobQueue
{
    ValueTask<EnqueueResult> EnqueueAsync<TJob>(
        TJob job,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
