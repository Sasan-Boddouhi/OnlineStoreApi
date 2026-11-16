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
/// Batch 11b — باقی‌ماندهٔ گاردهای Batch 11 پیش از بازیابی رمز (Batch 13):
///
///  • ادمین معمولی نمی‌تواند ایمیل یا موبایل یک مدیر ارشد را با PUT users/{id} عوض کند
///    (مسیر تصاحب حساب: موبایل/ایمیل جدید + بازیابی رمز) → 403 ADMIN_CANNOT_MODIFY_SUPERADMIN.
///  • ادمین معمولی نقش مدیر ارشد را نه به خودش و نه به دیگری نمی‌دهد (403 ADMIN_CANNOT_ASSIGN_SUPERADMIN).
///  • موبایل تکراری مانند ایمیل تکراری 409 است (USER_PHONE_EXISTS)، نه 400.
/// </summary>
public class Phase15Batch11bAdminUpdateGuardTests : ControllerIntegrationTestBase
{
    private const string AdminPhone = "09123456789";

    public Phase15Batch11bAdminUpdateGuardTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== ویرایش حساب مدیر ارشد ====================

    [Fact]
    public async Task Admin_CannotChangeSuperAdminEmail()
    {
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        var before = await LoadUserAsync(target);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}", token, new
        {
            UserId = target,
            PhoneNumber = before.PhoneNumber,
            FirstName = before.FirstName,
            LastName = before.LastName,
            Email = $"attacker-{Guid.NewGuid():N}@example.com"
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");
        var after = await LoadUserAsync(target);
        after.Email.Should().Be(before.Email);
        after.SecurityStamp.Should().Be(before.SecurityStamp);
    }

    [Fact]
    public async Task Admin_CannotChangeSuperAdminPhone()
    {
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        var before = await LoadUserAsync(target);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}", token, new
        {
            UserId = target,
            PhoneNumber = RandomPhone(),
            FirstName = before.FirstName,
            LastName = before.LastName
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");
        (await LoadUserAsync(target)).PhoneNumber.Should().Be(before.PhoneNumber);
    }

    [Fact]
    public async Task SuperAdmin_CanChangeAnotherSuperAdminEmail()
    {
        // کنترل مثبت: گارد فقط ادمینِ غیرمدیرارشد را می‌بندد.
        var adminId = await UserIdAsync(AdminPhone);
        await GrantRoleAsync(adminId, "SuperAdmin");
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        var before = await LoadUserAsync(target);
        var token = await GetAdminTokenAsync();
        var email = $"b11b-{Guid.NewGuid():N}@example.com";

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}", token, new
        {
            UserId = target,
            PhoneNumber = before.PhoneNumber,
            FirstName = before.FirstName,
            LastName = before.LastName,
            Email = email
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await LoadUserAsync(target)).Email.Should().Be(email);
    }

    [Fact]
    public async Task Admin_CanStillUpdateANonSuperAdminUser()
    {
        var target = await CreateUserWithRoleAsync("Customer");
        var before = await LoadUserAsync(target);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}", token, new
        {
            UserId = target,
            PhoneNumber = before.PhoneNumber,
            FirstName = "Renamed",
            LastName = before.LastName
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await LoadUserAsync(target)).FirstName.Should().Be("Renamed");
    }

    // ==================== اعطای نقش مدیر ارشد ====================

    [Fact]
    public async Task Admin_CannotGrantSuperAdminRole_ToSelfOrToAnotherUser()
    {
        var adminId = await UserIdAsync(AdminPhone);
        var customer = await CreateUserWithRoleAsync("Customer");
        var superAdminRoleId = await RoleIdAsync("SuperAdmin");
        var adminRoleId = await RoleIdAsync("Admin");
        var token = await GetAdminTokenAsync();

        var self = await SendAsync(HttpMethod.Put, $"/api/v1/users/{adminId}/roles", token,
            new { RoleIds = new[] { adminRoleId, superAdminRoleId } });
        await AssertProblemAsync(self, HttpStatusCode.Forbidden, "ADMIN_CANNOT_ASSIGN_SUPERADMIN");

        var other = await SendAsync(HttpMethod.Put, $"/api/v1/users/{customer}/roles", token,
            new { RoleIds = new[] { superAdminRoleId } });
        await AssertProblemAsync(other, HttpStatusCode.Forbidden, "ADMIN_CANNOT_ASSIGN_SUPERADMIN");

        (await HasRoleAsync(adminId, "SuperAdmin")).Should().BeFalse();
        (await HasRoleAsync(customer, "SuperAdmin")).Should().BeFalse();
    }

    [Fact]
    public async Task Admin_CannotStripSuperAdminRole_EvenWhenKeepingOtherRoles()
    {
        var target = await CreateUserWithRoleAsync("SuperAdmin");
        await CreateUserWithRoleAsync("SuperAdmin"); // گارد «آخرین مدیر ارشد» دخالت نکند
        var adminRoleId = await RoleIdAsync("Admin");
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target}/roles", token,
            new { RoleIds = new[] { adminRoleId } });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "ADMIN_CANNOT_MODIFY_SUPERADMIN");
        (await HasRoleAsync(target, "SuperAdmin")).Should().BeTrue();
    }

    // ==================== موبایل تکراری → 409 ====================

    [Fact]
    public async Task Register_DuplicatePhone_Returns409UserPhoneExists()
    {
        var phone = RandomPhone();
        (await RegisterAsync(phone)).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertProblemAsync(await RegisterAsync(phone), HttpStatusCode.Conflict, "USER_PHONE_EXISTS");
    }

    [Fact]
    public async Task AdminCreateAndUpdate_DuplicatePhone_Returns409UserPhoneExists()
    {
        var token = await GetAdminTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/users", token, new
        {
            PhoneNumber = AdminPhone,
            Password = "Strong@123",
            FirstName = "Dup",
            LastName = "Phone",
            DateOfBirth = "1370/01/01"
        });
        await AssertProblemAsync(create, HttpStatusCode.Conflict, "USER_PHONE_EXISTS");

        var other = await CreateUserWithRoleAsync("Customer");
        var before = await LoadUserAsync(other);
        var update = await SendAsync(HttpMethod.Put, $"/api/v1/users/{other}", token, new
        {
            UserId = other,
            PhoneNumber = AdminPhone,
            FirstName = before.FirstName,
            LastName = before.LastName
        });
        await AssertProblemAsync(update, HttpStatusCode.Conflict, "USER_PHONE_EXISTS");
    }

    // ==================== helpers ====================

    private Task<HttpResponseMessage> RegisterAsync(string phone)
        => Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Batch",
            LastName = "ElevenB",
            DeviceId = "b11b-device"
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
            FirstName = "Batch11b",
            LastName = roleCode,
            PhoneNumber = RandomPhone(),
            Email = $"b11b-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            UserType = roleCode == "Customer" ? UserType.Customer : UserType.Employee,
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

    private async Task<User> LoadUserAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.User.AsNoTracking().FirstAsync(u => u.UserId == userId);
    }

    private async Task<bool> HasRoleAsync(int userId, string roleCode)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserRole.AsNoTracking().AnyAsync(ur => ur.UserId == userId && ur.Role.Code == roleCode);
    }

    private static string RandomPhone() => $"0915{Random.Shared.Next(1000000, 9999999):D7}";
}
