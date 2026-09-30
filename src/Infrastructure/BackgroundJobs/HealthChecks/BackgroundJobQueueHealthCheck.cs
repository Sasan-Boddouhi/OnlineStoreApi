using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.BackgroundJobs.HealthChecks;

public sealed class BackgroundJobQueueHealthCheck : IHealthCheck
{
    private readonly ChannelBackgroundJobQueue _queue;

    public BackgroundJobQueueHealthCheck(ChannelBackgroundJobQueue queue)
    {
        _queue = queue;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _queue.IsShuttingDown
                ? HealthCheckResult.Unhealthy("Background job queue is shutting down.")
                : HealthCheckResult.Healthy());
    }
}
