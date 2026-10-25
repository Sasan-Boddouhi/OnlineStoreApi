using System.Security.Claims;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Online_Store_Application.Authorization;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Authorization;

/// <summary>
/// تست مستقیم PermissionHandler روی داده RBAC واقعی (TestDataSeed).
/// بدون این تست، کل مکانیزم مجوزدهی فاز ۱ بدون پوشش می‌ماند.
/// </summary>
public class PermissionHandlerTests : BaseIntegrationTest
{
    private AppDbContext DbContext => GetService<AppDbContext>();

    public PermissionHandlerTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private static ClaimsPrincipal CreateUser(params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private async Task<bool> IsGrantedAsync(string[] roles, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateUser(roles), null);
        var handler = new PermissionHandler(DbContext);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Admin_HasCatalogProductsWrite()
    {
        (await IsGrantedAsync(new[] { "Admin" }, Permissions.CatalogProductsWrite))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Manager_HasCatalogProductsWrite()
    {
        (await IsGrantedAsync(new[] { "Manager" }, Permissions.CatalogProductsWrite))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Customer_DoesNotHaveCatalogProductsWrite()
    {
        (await IsGrantedAsync(new[] { "Customer" }, Permissions.CatalogProductsWrite))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Seller_DoesNotHaveCatalogProductsWrite()
    {
        (await IsGrantedAsync(new[] { "Seller" }, Permissions.CatalogProductsWrite))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SuperAdmin_HasAdminFull()
    {
        (await IsGrantedAsync(new[] { "SuperAdmin" }, Permissions.AdminFull))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Admin_HasAdminFull_SoAdminOnlyPolicyStillWorks()
    {
        (await IsGrantedAsync(new[] { "Admin" }, Permissions.AdminFull))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Manager_DoesNotHaveAdminFull()
    {
        (await IsGrantedAsync(new[] { "Manager" }, Permissions.AdminFull))
            .Should().BeFalse();
    }

    [Fact]
    public async Task UserWithMultipleRoles_GrantedWhenAnyRoleHasPermission()
    {
        (await IsGrantedAsync(new[] { "Customer", "Manager" }, Permissions.CatalogProductsWrite))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UnknownRole_IsDenied()
    {
        (await IsGrantedAsync(new[] { "GhostRole" }, Permissions.CatalogProductsWrite))
            .Should().BeFalse();
    }

    [Fact]
    public async Task UnauthenticatedUser_IsDenied()
    {
        var requirement = new PermissionRequirement(Permissions.CatalogProductsWrite);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var context = new AuthorizationHandlerContext(new[] { requirement }, anonymous, null);

        await new PermissionHandler(DbContext).HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}