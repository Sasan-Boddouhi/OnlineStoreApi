using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 11 — گاردهای مدیریت کاربر (F-22) و اعتبارسنجی ثبت‌نام (بخشی از F-41).
///
///  • ادمین نمی‌تواند حساب خودش را غیرفعال یا حذف کند (409 CANNOT_DEACTIVATE_SELF).
///  • آخرین مدیر ارشد فعال قابل غیرفعال‌سازی، حذف یا تنزل نقش نیست (409 LAST_SUPERADMIN).
///  • ادمین معمولی نمی‌تواند وضعیت، نقش یا وجود یک مدیر ارشد را تغییر دهد (403 ADMIN_CANNOT_MODIFY_SUPERADMIN).
///  • ایمیل تکراری در ثبت‌نام، ایجاد و ویرایش کاربر 409 USER_EMAIL_EXISTS است؛ تاریخ شمسی نامعتبر 422 است، نه 500.
/// </summary>
public class Phase15Batch11AdminGuardTests : ControllerIntegrationTestBase
{
    private const string AdminPhone = "09123456789";

    public Phase15Batch11AdminGuardTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== خود ادمین ====================

    [Fact]
    public async Task Admin_CannotDeactivateOrDeleteOwnAccount()
    {
        var adminId = await UserIdAsync(AdminPhone);
        var token = await GetAdminTokenAsync();

        var patch = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{adminId}", token, new { userId = adminId, isActive = false });
        await AssertProblemAsync(patch, HttpStatusCode.Conflict, "CANNOT_DEACTIVATE_SELF");

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/users/{adminId}", token);
        await AssertProblemAsync(delete, HttpStatusCode.Conflict, "CANNOT_DEACTIVATE_SELF");

        (await IsActiveAsync(adminId)).Should().BeTrue();
        (await SendAsync(HttpMethod.Get, "/api/v1/auth/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== آخرین مدیر ارشد ====================

    [Fact]
    public async Task LastSuperAdmin_CannotBeDeactivatedDeletedOrDemoted()
    {
        var superAdminId = await CreateUserWithRoleAsync("SuperAdmin");
        var token = await GetAdminTokenAsync();
        var customerRoleId = await RoleIdAsync("Customer");

        var patch = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{superAdminId}", token, new { userId = superAdminId, isActive = false });
        await AssertProblemAsync(patch, HttpStatusCode.Conflict, "LAST_SUPERADMIN");

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/users/{superAdminId}", token);
        await AssertProblemAsync(delete, HttpStatusCode.Conflict, "LAST_SUPERADMIN");

        var demote = await SendAsync(HttpMethod.Put, $"/api/v1/users/{superAdminId}/roles", token, new { RoleIds = new[] { customerRoleId } });
        await AssertProblemAsync(demote, HttpStatusCode.Conflict, "LAST_SUPERADMIN");

        (await IsActiveAsync(superAdminId)).Should().BeTrue();
        (await HasRoleAsync(superAdminId, "SuperAdmin")).Should().BeTrue();
    }

    // ==================== ادمین معمولی در برابر مدیر ارشد ====================

    [Fact]
    public async Task Admin_CannotDeactivateDeleteOrDemoteASuperAdmin()
    {
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        await CreateUserWithRoleAsync("SuperAdmin"); // مدیر ارشد دوم، تا گارد «آخرین» دخالت نکند
        var token = await GetAdminTokenAsync();
        var customerRoleId = await RoleIdAsync("Customer");

        var patch = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{target}", token, new { userId = target, isActive = false });
        await AssertProblemAsync(patch, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/users/{target}", token);
        await AssertProblemAsync(delete, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");

        var demote = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}/roles", token, new { RoleIds = new[] { customerRoleId } });
        await AssertProblemAsync(demote, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");

        (await IsActiveAsync(target)).Should().BeTrue();
        (await HasRoleAsync(target, "SuperAdmin")).Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_CanDeactivateAnotherSuperAdmin_WhenOneRemains()
    {
        // کنترل مثبت: گاردها مدیر ارشد را از مدیریت مدیر ارشد دیگر باز نمی‌دارند.
        var adminId = await UserIdAsync(AdminPhone);
        await GrantRoleAsync(adminId, "SuperAdmin");
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        var token = await GetAdminTokenAsync();

        var patch = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{target}", token, new { userId = target, isActive = false });

        patch.StatusCode.Should().Be(HttpStatusCode.NoContent, await patch.Content.ReadAsStringAsync());
        (await IsActiveAsync(target)).Should().BeFalse();
    }

    // ==================== F-41: ایمیل تکراری و تاریخ نامعتبر ====================

    [Fact]
    public async Task Register_DuplicateEmail_Returns409UserEmailExists()
    {
        var email = $"b11-{Guid.NewGuid():N}@example.com";
        (await RegisterAsync(RandomPhone(), email)).StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicate = await RegisterAsync(RandomPhone(), email);

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "USER_EMAIL_EXISTS");
    }

    [Fact]
    public async Task AdminCreateAndUpdate_DuplicateEmail_Returns409UserEmailExists()
    {
        var email = $"b11-{Guid.NewGuid():N}@example.com";
        (await RegisterAsync(RandomPhone(), email)).StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await GetAdminTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/users", token, new
        {
            PhoneNumber = RandomPhone(),
            Password = "Strong@123",
            FirstName = "Dup",
            LastName = "Email",
            Email = email,
            DateOfBirth = "1370/01/01"
        });
        await AssertProblemAsync(create, HttpStatusCode.Conflict, "USER_EMAIL_EXISTS");

        var otherPhone = RandomPhone();
        (await RegisterAsync(otherPhone, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var otherId = await UserIdAsync(otherPhone);
        var update = await SendAsync(HttpMethod.Put, $"/api/v1/users/{otherId}", token, new
        {
            UserId = otherId,
            PhoneNumber = otherPhone,
            FirstName = "Other",
            LastName = "User",
            Email = email,
            DateOfBirth = "1370/01/01"
        });
        await AssertProblemAsync(update, HttpStatusCode.Conflict, "USER_EMAIL_EXISTS");
    }

    [Fact]
    public async Task Register_InvalidPersianDate_Returns422_Not500()
    {
        var response = await RegisterAsync(RandomPhone(), null, dateOfBirth: "1400/13/40");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    // ==================== helpers ====================

    private Task<HttpResponseMessage> RegisterAsync(string phone, string? email, string? dateOfBirth = null)
        => Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Batch",
            LastName = "Eleven",
            Email = email,
            DateOfBirth = dateOfBirth,
            DeviceId = "b11-device"
        });

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return Client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private async Task<int> CreateUserWithRoleAsync(string roleCode)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            FirstName = "Batch11",
            LastName = roleCode,
            PhoneNumber = RandomPhone(),
            PasswordHash = "not-used",
            UserType = UserType.Employee,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Role.FirstAsync(r => r.Code == roleCode);
        db.UserRole.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
        await db.SaveChangesAsync();
        return user.UserId;
    }

    private async Task GrantRoleAsync(int userId, string roleCode)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Role.FirstAsync(r => r.Code == roleCode);
        db.UserRole.Add(new UserRole { UserId = userId, RoleId = role.RoleId });
        await db.SaveChangesAsync();
    }

    private async Task<int> RoleIdAsync(string roleCode)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Role.AsNoTracking().FirstAsync(r => r.Code == roleCode)).RoleId;
    }

    private async Task<int> UserIdAsync(string phone)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.User.AsNoTracking().FirstAsync(u => u.PhoneNumber == phone)).UserId;
    }

    private async Task<bool> IsActiveAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.User.AsNoTracking().FirstAsync(u => u.UserId == userId)).IsActive;
    }

    private async Task<bool> HasRoleAsync(int userId, string roleCode)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserRole.AsNoTracking().AnyAsync(ur => ur.UserId == userId && ur.Role.Code == roleCode);
    }

    private static string RandomPhone() => $"0914{Random.Shared.Next(1000000, 9999999):D7}";
}
