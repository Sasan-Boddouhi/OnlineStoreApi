using System.Collections.Concurrent;
using Application.Interfaces.Services;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class FakeCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        _cache.TryGetValue(key, out var value);
        return Task.FromResult(value as T);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        _cache[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}