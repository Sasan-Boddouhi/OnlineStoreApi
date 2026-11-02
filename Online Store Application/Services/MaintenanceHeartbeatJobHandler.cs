using Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Online_Store_Application.Services;

/// <summary>
/// M7.7 — هندلر جاب پایش صف پس‌زمینه.
/// </summary>
public sealed class MaintenanceHeartbeatJobHandler : IBackgroundJobHandler<MaintenanceHeartbeatJob>
{
    private readonly ILogger<MaintenanceHeartbeatJobHandler> _logger;

    public MaintenanceHeartbeatJobHandler(ILogger<MaintenanceHeartbeatJobHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(
        MaintenanceHeartbeatJob job,
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "MaintenanceHeartbeatJob processed: Origin={Origin}, CreatedAt={CreatedAt}, Attempt={Attempt}",
            job.Origin, job.CreatedAtUtc, context.Attempt);
        return Task.CompletedTask;
    }
}
