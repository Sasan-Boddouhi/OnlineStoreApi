using System.Security.Claims;
using Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Online_Store_Application.Services;

namespace OnlineStore.Tests.Unit.Services;

public class CurrentUserServiceTests
{
    private Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private CurrentUserService _service;

    public CurrentUserServiceTests()
    {
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _service = new CurrentUserService(_httpContextAccessorMock.Object);
    }

    private void SetupHttpContext(string? userIdClaim = null, string? roleClaim = null, bool isAuthenticated = true)
    {
        var user = new ClaimsPrincipal();
        if (isAuthenticated)
        {
            var claims = new List<Claim>();
            if (userIdClaim != null)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdClaim));
            if (roleClaim != null)
                claims.Add(new Claim(ClaimTypes.Role, roleClaim));
            user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        var context = new DefaultHttpContext { User = user };
        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(context);
    }

    [Fact]
    public void GetCurrentUserId_Authenticated_ReturnsId()
    {
        SetupHttpContext(userIdClaim: "123", isAuthenticated: true);
        _service.GetCurrentUserId().Should().Be(123);
    }

    [Fact]
    public void GetCurrentUserId_NotAuthenticated_Throws()
    {
        SetupHttpContext(userIdClaim: null, isAuthenticated: false);
        _service.Invoking(s => s.GetCurrentUserId()).Should().Throw<Exception>().WithMessage("*احراز هویت*");
    }

    [Fact]
    public void TryGetCurrentUserId_ReturnsNull_WhenNotAuthenticated()
    {
        SetupHttpContext(userIdClaim: null, isAuthenticated: false);
        _service.TryGetCurrentUserId().Should().BeNull();
    }

    [Fact]
    public void GetCurrentUserRole_ReturnsRole()
    {
        SetupHttpContext(roleClaim: "Admin");
        _service.GetCurrentUserRole().Should().Be("Admin");
    }

    [Fact]
    public void GetCurrentUserName_ReturnsName()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "TestAuth"));
        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(new DefaultHttpContext { User = user });
        _service.GetCurrentUserName().Should().Be("TestUser");
    }
}