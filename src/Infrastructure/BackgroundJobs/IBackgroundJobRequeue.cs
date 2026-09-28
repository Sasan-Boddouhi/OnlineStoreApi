using Application.BackgroundJobs;

namespace Infrastructure.BackgroundJobs;

internal interface IBackgroundJobRequeue
{
    ValueTask<EnqueueStatus> RequeueAsync(
        BackgroundJobEnvelope envelope,
        CancellationToken cancellationToken);
}
