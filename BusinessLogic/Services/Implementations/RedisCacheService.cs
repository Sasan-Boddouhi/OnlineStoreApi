using System.Diagnostics;
using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// کش توزیع‌شده روی Redis.
/// F-23: کش فقط یک بهینه‌سازی است؛ خطای Redis ثبت می‌شود و درخواست بدون کش ادامه می‌یابد
/// (Get → miss، Set/Remove → بی‌اثر). پیش از این هر خطای Redis به 500 روی کاتالوگ و users/me تبدیل می‌شد.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private static readonly ActivitySource ActivitySource = new("OnlineStore.Cache");

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        using var activity = ActivitySource.StartActivity("Cache.Get", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.type", typeof(T).Name);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cached = await _cache.GetStringAsync(key, ct);

            stopwatch.Stop();
            OnlineStoreMetrics.CacheDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", "get"),
                new KeyValuePair<string, object?>("cache.type", typeof(T).Name));

            if (cached == null)
            {
                activity?.SetTag("cache.hit", false);
                OnlineStoreMetrics.CacheMisses.Add(
                    1,
                    new KeyValuePair<string, object?>("cache.type", typeof(T).Name));
                activity?.SetStatus(ActivityStatusCode.Ok);
                return null;
            }

            activity?.SetTag("cache.hit", true);
            OnlineStoreMetrics.CacheHits.Add(
                1,
                new KeyValuePair<string, object?>("cache.type", typeof(T).Name));
            activity?.SetStatus(ActivityStatusCode.Ok);
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("cache.hit", false);
            _logger.LogWarning(ex, "Cache read failed for key {CacheKey}; treating as a miss", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        using var activity = ActivitySource.StartActivity("Cache.Set", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.type", typeof(T).Name);
        if (expiry.HasValue)
            activity?.SetTag("cache.expiry_seconds", expiry.Value.TotalSeconds);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var options = new DistributedCacheEntryOptions();
            if (expiry.HasValue)
                options.AbsoluteExpirationRelativeToNow = expiry;

            var serialized = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, serialized, options, ct);

            stopwatch.Stop();
            OnlineStoreMetrics.CacheDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", "set"),
                new KeyValuePair<string, object?>("cache.type", typeof(T).Name));

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Cache write failed for key {CacheKey}; continuing without cache", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("Cache.Remove", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _cache.RemoveAsync(key, ct);

            stopwatch.Stop();
            OnlineStoreMetrics.CacheDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", "remove"));

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // ورودی کهنه حداکثر تا پایان TTL خودش باقی می‌ماند.
            _logger.LogError(ex, "Cache remove failed for key {CacheKey}; entry may stay stale until it expires", key);
        }
    }

    /// <summary>لغو درخواست توسط فراخوان خطای کش نیست و باید بالا برود.</summary>
    private static bool IsCallerCancellation(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;
}
