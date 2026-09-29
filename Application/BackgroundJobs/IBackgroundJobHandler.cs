namespace Application.BackgroundJobs;

public interface IBackgroundJobHandler<in TJob>
{
    Task HandleAsync(
        TJob job,
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken);
}
