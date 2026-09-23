using System.Diagnostics;
using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Application.Diagnostics;

namespace BusinessLogic.Services.Implementations;

public sealed class RedisCacheService : ICacheService
{
    private static readonly ActivitySource ActivitySource = new("OnlineStore.Cache");

    private readonly IDistributedCache _cache;

    public RedisCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        using var activity = ActivitySource.StartActivity("Cache.Get", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.type", typeof(T).Name);

        try
        {
            var cached = await _cache.GetStringAsync(key, ct);

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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        using var activity = ActivitySource.StartActivity("Cache.Set", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.type", typeof(T).Name);
        if (expiry.HasValue)
            activity?.SetTag("cache.expiry_seconds", expiry.Value.TotalSeconds);

        try
        {
            var options = new DistributedCacheEntryOptions();
            if (expiry.HasValue)
                options.AbsoluteExpirationRelativeToNow = expiry;

            var serialized = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, serialized, options, ct);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("Cache.Remove", ActivityKind.Internal);
        activity?.SetTag("cache.key", key);

        try
        {
            await _cache.RemoveAsync(key, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}