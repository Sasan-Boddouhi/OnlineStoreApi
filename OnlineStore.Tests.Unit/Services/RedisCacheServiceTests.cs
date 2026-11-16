using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

/// <summary>
/// F-23: قطعی Redis نباید درخواست را بشکند؛ Get → miss، Set/Remove → بی‌اثر.
/// </summary>
public class RedisCacheServiceTests
{
    private readonly Mock<IDistributedCache> _cacheMock = new();

    private RedisCacheService CreateService()
        => new(_cacheMock.Object, NullLogger<RedisCacheService>.Instance);

    private static Exception RedisDown()
        => new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis is down");

    [Fact]
    public async Task GetAsync_WhenRedisThrows_ReturnsNull()
    {
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(RedisDown());

        var result = await CreateService().GetAsync<string>("products:version");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenCachedValueIsCorrupt_ReturnsNull()
    {
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{not json"u8.ToArray());

        var result = await CreateService().GetAsync<List<string>>("key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenRedisThrows_DoesNotThrow()
    {
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(RedisDown());

        var act = () => CreateService().SetAsync("key", "value", TimeSpan.FromMinutes(1));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_WhenRedisThrows_DoesNotThrow()
    {
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(RedisDown());

        var act = () => CreateService().RemoveAsync("key");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => CreateService().GetAsync<string>("key", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
