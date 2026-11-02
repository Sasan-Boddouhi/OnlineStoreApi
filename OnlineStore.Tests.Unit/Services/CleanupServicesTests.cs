using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Online_Store_Application.Services;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

public class CleanupServicesTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    public CleanupServicesTests()
    {
        _scopeFactoryMock.Setup(s => s.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IUnitOfWork))).Returns(_uowMock.Object);
    }

    [Fact]
    public async Task CartCleanupService_DeletesStaleGuestCarts()
    {
        var cartRepoMock = new Mock<IGenericRepository<Cart>>();
        _uowMock.Setup(u => u.Repository<Cart>()).Returns(cartRepoMock.Object);

        var staleCarts = new List<Cart>
        {
            new Cart { CartId = 1, UserId = null, ModifiedOn = DateTime.UtcNow.AddDays(-10) },
            new Cart { CartId = 2, UserId = null, ModifiedOn = DateTime.UtcNow.AddDays(-8) }
        };

        cartRepoMock.Setup(r => r.ListAsync(It.IsAny<Spec<Cart>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleCarts);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var service = new CartCleanupService(_scopeFactoryMock.Object, Mock.Of<ILogger<CartCleanupService>>());
        var deletedCount = await service.RunCleanupAsync(CancellationToken.None);

        deletedCount.Should().Be(2);
        cartRepoMock.Verify(r => r.DeleteRange(staleCarts), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpiredTokenCleanupService_PurgesStaleRevokedTokens()
    {
        var tokenRepoMock = new Mock<IGenericRepository<RefreshTokenEntity>>();
        _uowMock.Setup(u => u.Repository<RefreshTokenEntity>()).Returns(tokenRepoMock.Object);

        var staleTokens = new List<RefreshTokenEntity>
        {
            new RefreshTokenEntity { Id = 1, IsRevoked = true, ExpiryDate = DateTime.UtcNow.AddDays(-35), ReplacedByTokenId = 2 },
            new RefreshTokenEntity { Id = 2, IsRevoked = true, ExpiryDate = DateTime.UtcNow.AddDays(-40) }
        };

        tokenRepoMock.Setup(r => r.ListAsync(It.IsAny<Spec<RefreshTokenEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleTokens);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var service = new ExpiredTokenCleanupService(_scopeFactoryMock.Object, Mock.Of<ILogger<ExpiredTokenCleanupService>>());
        var deletedCount = await service.RunCleanupAsync(CancellationToken.None);

        deletedCount.Should().Be(2);
        staleTokens[0].ReplacedByTokenId.Should().BeNull();
        tokenRepoMock.Verify(r => r.DeleteRange(staleTokens), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
