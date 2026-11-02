namespace Application.BackgroundJobs;

/// <summary>
/// M7.7 — جاب پس‌زمینه برای بررسی سلامت و پایش کارکرد صف پس‌زمینه در سیستم.
/// </summary>
public sealed record MaintenanceHeartbeatJob(string Origin, DateTime CreatedAtUtc);
