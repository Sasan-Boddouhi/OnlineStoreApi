using System.Diagnostics.Metrics;

namespace Application.Diagnostics;

/// <summary>
/// Central registry of all custom metrics.
/// Meters are registered via AddMeter() in OpenTelemetryExtensions.
/// </summary>
public static class OnlineStoreMetrics
{
    public const string AuthMeterName = "OnlineStore.Auth";
    public const string CacheMeterName = "OnlineStore.Cache";

    public static readonly Meter AuthMeter = new(AuthMeterName, "1.0.0");
    public static readonly Meter CacheMeter = new(CacheMeterName, "1.0.0");

    // ══════════════════════════════════════════════════════════
    // Auth Metrics
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Total login attempts (success + failure).
    /// </summary>
    public static readonly Counter<long> LoginAttempts =
        AuthMeter.CreateCounter<long>(
            name: "auth.login.attempts",
            unit: "attempts",
            description: "Total number of login attempts");

    /// <summary>
    /// Successful logins.
    /// </summary>
    public static readonly Counter<long> LoginSuccess =
        AuthMeter.CreateCounter<long>(
            name: "auth.login.success",
            unit: "logins",
            description: "Number of successful logins");

    /// <summary>
    /// Failed logins with reason tag.
    /// Reason: "invalid_password", "user_not_found", "account_locked"
    /// </summary>
    public static readonly Counter<long> LoginFailure =
        AuthMeter.CreateCounter<long>(
            name: "auth.login.failure",
            unit: "failures",
            description: "Number of failed logins");

    /// <summary>
    /// Refresh token reuse detected (security event).
    /// </summary>
    public static readonly Counter<long> RefreshTokenReuse =
        AuthMeter.CreateCounter<long>(
            name: "auth.refresh.reuse_detected",
            unit: "events",
            description: "Number of refresh token reuse attempts");

    /// <summary>
    /// User registrations.
    /// </summary>
    public static readonly Counter<long> UserRegistrations =
        AuthMeter.CreateCounter<long>(
            name: "auth.register.success",
            unit: "registrations",
            description: "Number of successful registrations");

    // ══════════════════════════════════════════════════════════
    // Cache Metrics
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Cache hits.
    /// </summary>
    public static readonly Counter<long> CacheHits =
        CacheMeter.CreateCounter<long>(
            name: "cache.hit",
            unit: "hits",
            description: "Number of cache hits");

    /// <summary>
    /// Cache misses.
    /// </summary>
    public static readonly Counter<long> CacheMisses =
        CacheMeter.CreateCounter<long>(
            name: "cache.miss",
            unit: "misses",
            description: "Number of cache misses");

    /// <summary>
    /// Cache operations duration.
    /// </summary>
    public static readonly Histogram<double> CacheDuration =
        CacheMeter.CreateHistogram<double>(
            name: "cache.operation.duration",
            unit: "ms",
            description: "Duration of cache operations in milliseconds");


    // ══════════════════════════════════════════════════════════
    // Background Jobs Metrics
    // ══════════════════════════════════════════════════════════

    public const string BackgroundJobsMeterName = "OnlineStore.BackgroundJobs";

    public static readonly Meter BackgroundJobsMeter =
        new(BackgroundJobsMeterName, "1.0.0");

    /// <summary>
    /// Total jobs successfully written to the main execution queue.
    /// Does not count retry re-enqueues.
    /// </summary>
    public static readonly Counter<long> BackgroundJobsEnqueued =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.enqueued",
            unit: "{jobs}",
            description: "Total jobs written to the main execution queue");

    /// <summary>
    /// Total jobs that completed successfully.
    /// </summary>
    public static readonly Counter<long> BackgroundJobsCompleted =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.completed",
            unit: "{jobs}",
            description: "Total background jobs completed successfully");

    /// <summary>
    /// Total jobs that failed permanently (no retry).
    /// </summary>
    public static readonly Counter<long> BackgroundJobsFailed =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.failed",
            unit: "{jobs}",
            description: "Total background jobs that failed permanently");

    /// <summary>
    /// Total cancelled jobs. Reason is a JobCancellationReason value.
    /// </summary>
    public static readonly Counter<long> BackgroundJobsCancelled =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.cancelled",
            unit: "{jobs}",
            description: "Total cancelled background jobs");

    /// <summary>
    /// Total jobs scheduled for retry.
    /// </summary>
    public static readonly Counter<long> BackgroundJobsRetried =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.retried",
            unit: "{jobs}",
            description: "Total background jobs scheduled for retry");

    /// <summary>
    /// Total enqueue attempts rejected. Reason: "QueueFull" or "ShuttingDown".
    /// </summary>
    public static readonly Counter<long> BackgroundJobsEnqueueRejected =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.enqueue_rejected",
            unit: "{jobs}",
            description: "Total enqueue attempts rejected by the queue");

    /// <summary>
    /// Duration of background job execution. Outcome: "succeeded", "cancelled", "exception".
    /// </summary>
    public static readonly Histogram<double> BackgroundJobsExecutionDuration =
        BackgroundJobsMeter.CreateHistogram<double>(
            name: "background_jobs.execution.duration",
            unit: "s",
            description: "Duration of background job execution");

    /// <summary>
    /// Computed retry delay for scheduled retries.
    /// </summary>
    public static readonly Histogram<double> BackgroundJobsRetryDelay =
        BackgroundJobsMeter.CreateHistogram<double>(
            name: "background_jobs.retry.delay",
            unit: "s",
            description: "Computed retry delay for scheduled retries");

    /// <summary>
    /// Total retry queue starvation events.
    /// </summary>
    public static readonly Counter<long> BackgroundJobsRetryQueueStarvation =
        BackgroundJobsMeter.CreateCounter<long>(
            name: "background_jobs.retry_queue.starvation",
            unit: "{jobs}",
            description: "Total retry queue starvation events");
}
