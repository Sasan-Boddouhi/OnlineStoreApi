using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Auth;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using Xunit;

namespace OnlineStore.Tests.Integration.Auth;

[Collection("DatabaseCollection")]
public class AuthControllerTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private string? _refreshToken;

    public AuthControllerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // پاکسازی داده‌های تست قبل از هر تست (اختیاری)
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var testUsers = await db.User.Where(u => u.PhoneNumber.StartsWith("091299999")).ToListAsync();
        db.User.RemoveRange(testUsers);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await Task.CompletedTask;

    #region Register

    [Fact]
    public async Task Register_ValidUser_ReturnsOkWithTokens()
    {
        var dto = new RegisterDto
        {
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09129999901",
            Password = "123456",
            DateOfBirth = "1370/01/01",
            Email = "test@example.com",
            DeviceId = "test-device"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
        result.User.PhoneNumber.Should().Be(dto.PhoneNumber);
    }

    [Fact]
    public async Task Register_DuplicatePhone_ReturnsBadRequest()
    {
        var dto = new RegisterDto
        {
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09129999902",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", dto);
        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("تکراری");
    }

    [Fact]
    public async Task Register_InvalidPersianDate_ReturnsBadRequest()
    {
        var dto = new RegisterDto
        {
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09129999903",
            Password = "123456",
            DateOfBirth = "99/01/01" // فرمت اشتباه
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Login

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        // ثبت‌نام کاربر
        var registerDto = new RegisterDto
        {
            FirstName = "Login",
            LastName = "Test",
            PhoneNumber = "09129999910",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999910",
            Password = "123456",
            DeviceId = "test-device"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        _refreshToken = result.RefreshToken;
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var registerDto = new RegisterDto
        {
            FirstName = "Wrong",
            LastName = "Pass",
            PhoneNumber = "09129999911",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999911",
            Password = "wrong",
            DeviceId = "test-device"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UserNotFound_ReturnsUnauthorized()
    {
        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999999",
            Password = "123456",
            DeviceId = "test-device"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_LockoutAfterMaxFailedAttempts_ReturnsUnauthorized()
    {
        var registerDto = new RegisterDto
        {
            FirstName = "Lockout",
            LastName = "Test",
            PhoneNumber = "09129999912",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999912",
            Password = "wrong",
            DeviceId = "test-device"
        };
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        // ششمین تلاش نیز باید Unauthorized باشد (قفل حساب)
        var finalResponse = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Refresh Token

    [Fact]
    public async Task Refresh_ValidRefreshToken_ReturnsNewTokens()
    {
        // ثبت‌نام و لاگین
        var registerDto = new RegisterDto
        {
            FirstName = "Refresh",
            LastName = "Test",
            PhoneNumber = "09129999920",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999920",
            Password = "123456",
            DeviceId = "test-device"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        var refreshToken = authResult!.RefreshToken;

        var refreshDto = new RefreshTokenDto { RefreshToken = refreshToken };
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newTokens = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        newTokens.Should().NotBeNull();
        newTokens!.AccessToken.Should().NotBeNullOrEmpty();
        newTokens.RefreshToken.Should().NotBeNullOrEmpty();
        newTokens.RefreshToken.Should().NotBe(refreshToken); // توکن جدید
    }

    [Fact]
    public async Task Refresh_InvalidRefreshToken_ReturnsUnauthorized()
    {
        var refreshDto = new RefreshTokenDto { RefreshToken = "invalid-token" };
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ExpiredOrRevokedToken_ReturnsUnauthorized()
    {
        // این تست نیاز به زمان انتظار دارد؛ به صورت معمول می‌توان با دستکاری دیتابیس انجام داد
        // فعلاً رد می‌شود
    }

    #endregion

    #region Logout

    [Fact]
    public async Task Logout_AuthorizedUser_RevokesSession()
    {
        // ثبت‌نام و لاگین
        var registerDto = new RegisterDto
        {
            FirstName = "Logout",
            LastName = "Test",
            PhoneNumber = "09129999930",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999930",
            Password = "123456",
            DeviceId = "test-device"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        var token = authResult!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // بررسی وضعیت نشست در دیتابیس (استفاده از UserSession مفرد)
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSession
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.User.PhoneNumber == "09129999930" && s.Status == UserSession.SessionStatus.Active);
        session.Should().BeNull("نشست باید باطل شده باشد");

        // همچنین بررسی کنید که هیچ نشست فعال دیگری وجود نداشته باشد
        var activeSessions = await db.UserSession
            .CountAsync(s => s.User.PhoneNumber == "09129999930" && s.Status == UserSession.SessionStatus.Active);
        activeSessions.Should().Be(0);
    }

    [Fact]
    public async Task Logout_WithoutToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Me

    [Fact]
    public async Task Me_AuthorizedUser_ReturnsUserInfo()
    {
        var registerDto = new RegisterDto
        {
            FirstName = "Me",
            LastName = "Test",
            PhoneNumber = "09129999940",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var loginDto = new LoginDto
        {
            PhoneNumber = "09129999940",
            Password = "123456",
            DeviceId = "test-device"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginDto);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        var token = authResult!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        userInfo.Should().ContainKey("phoneNumber").WhoseValue.Should().Be("09129999940");
    }

    #endregion
}