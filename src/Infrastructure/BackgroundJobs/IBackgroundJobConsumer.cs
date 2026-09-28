using Application.BackgroundJobs;

namespace Infrastructure.BackgroundJobs;

public interface IBackgroundJobConsumer
{
    ValueTask<BackgroundJobEnvelope> DequeueAsync(
        CancellationToken cancellationToken);
}
