using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.BackgroundJobs.HealthChecks;

public sealed class BackgroundJobSchedulerHealthCheck : IHealthCheck
{
    private readonly RetryScheduler _scheduler;

    public BackgroundJobSchedulerHealthCheck(RetryScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _scheduler.ExecuteTask?.IsCompleted != true
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Background job scheduler is not running."));
    }
}
