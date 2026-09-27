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
}
