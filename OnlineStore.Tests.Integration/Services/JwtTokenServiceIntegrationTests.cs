using Application.Interfaces.Security;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using System.Security.Claims;

namespace OnlineStore.Tests.Integration.Services;

public class JwtTokenServiceIntegrationTests : BaseIntegrationTest
{
    private IJwtTokenService JwtTokenService => GetService<IJwtTokenService>();

    public JwtTokenServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new("SessionId", Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, "Admin")
        };

        var token = JwtTokenService.GenerateToken(claims);
        token.Should().NotBeNullOrEmpty();
        // JWT has 3 parts separated by dots
        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        var token = JwtTokenService.GenerateRefreshToken();
        token.Should().NotBeNullOrEmpty();
        // Refresh tokens are usually long random strings
        token.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public void GenerateToken_WithMultipleClaims_ProducesDifferentTokens()
    {
        var claims1 = new List<Claim> { new("role", "admin") };
        var claims2 = new List<Claim> { new("role", "user") };

        var token1 = JwtTokenService.GenerateToken(claims1);
        var token2 = JwtTokenService.GenerateToken(claims2);

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateRefreshToken_ProducesUniqueTokens()
    {
        var token1 = JwtTokenService.GenerateRefreshToken();
        var token2 = JwtTokenService.GenerateRefreshToken();

        token1.Should().NotBe(token2);
    }
}