using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace BusinessLogic.Services.Implementations;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public RedisCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var cached = await _cache.GetStringAsync(key, ct);
        return cached == null ? null : JsonSerializer.Deserialize<T>(cached);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiry;

        var serialized = JsonSerializer.Serialize(value);
        await _cache.SetStringAsync(key, serialized, options, ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(key, ct);
    }
}