using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Auth;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Controllers;

public class AuthControllerIntegrationTests : ControllerIntegrationTestBase
{
    public AuthControllerIntegrationTests(IntegrationTestFactory<Program> factory)
        : base(factory) { }

    // ===============================================
    // LOGIN
    // ===============================================
    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var loginDto = new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "test-device"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorized()
    {
        var loginDto = new
        {
            PhoneNumber = "09123456789",
            Password = "WrongPass",
            DeviceId = "test-device"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_MissingDeviceId_ReturnsBadRequest()
    {
        // بسته به FluentValidation ممکن است DeviceId اجباری باشد
        var loginDto = new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123"
            // DeviceId omitted
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ===============================================
    // REGISTER
    // ===============================================
    [Fact]
    public async Task Register_NewUser_ReturnsOk()
    {
        var registerDto = new
        {
            PhoneNumber = "09120001122",
            Password = "NewUser@123",
            FirstName = "کاربر",
            LastName = "جدید",
            DeviceId = "test-device",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/register", registerDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicatePhone_ReturnsBadRequest()
    {
        var registerDto = new
        {
            PhoneNumber = "09120001133",
            Password = "Test@123",
            FirstName = "اول",
            LastName = "کاربر",
            DeviceId = "test-device",
            DateOfBirth = "1370/01/01"
        };

        // ثبت اول
        await Client.PostAsJsonAsync("/api/auth/register", registerDto);

        // تلاش دوباره با همان شماره
        var response = await Client.PostAsJsonAsync("/api/auth/register", registerDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_MissingRequiredField_ReturnsBadRequest()
    {
        // مثلاً بدون DateOfBirth که اجباری است
        var registerDto = new
        {
            PhoneNumber = "09120009999",
            Password = "Test@123",
            FirstName = "ناقص",
            LastName = "کاربر",
            DeviceId = "test-device"
            // DateOfBirth حذف شده
        };

        var response = await Client.PostAsJsonAsync("/api/auth/register", registerDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ===============================================
    // ME
    // ===============================================
    [Fact]
    public async Task Me_Authenticated_ReturnsUserInfo()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"userId\"");
        content.Should().Contain("\"fullName\"");
        content.Should().Contain("\"role\"");
        content.Should().Contain("\"phoneNumber\"");
    }

    [Fact]
    public async Task Me_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ===============================================
    // REFRESH TOKEN
    // ===============================================
    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokens()
    {
        // ابتدا لاگین کن و refreshToken بگیر
        var loginDto = new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "test-device"
        };
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();

        // حالا refresh
        var refreshDto = new { RefreshToken = authResult!.RefreshToken };
        var response = await Client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var newAuth = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        newAuth!.AccessToken.Should().NotBeNullOrEmpty();
        newAuth.RefreshToken.Should().NotBeNullOrEmpty();
        // توکن‌ها باید با قبلی متفاوت باشند (اختیاری)
    }

    [Fact]
    public async Task Refresh_InvalidToken_ReturnsUnauthorized()
    {
        var refreshDto = new { RefreshToken = "invalid-refresh-token" };
        var response = await Client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ExpiredOrRevokedToken_ReturnsUnauthorized()
    {
        // یک توکن refresh بگیر
        var loginDto = new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "test-device"
        };
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();

        // با همان توکن دوباره refresh کن (باید یک‌بار مصرف باشد)
        var refreshDto = new { RefreshToken = authResult!.RefreshToken };
        var firstRefresh = await Client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK); // بار اول موفق

        var secondRefresh = await Client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        // بنابر پیاده‌سازی AuthService، توکن قبلی revoked می‌شود
        secondRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ===============================================
    // LOGOUT
    // ===============================================
    [Fact]
    public async Task Logout_ValidToken_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // بعد از خروج، همان توکن دیگر معتبر نیست
        var meResponse = await Client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutToken_ReturnsUnauthorized()
    {
        var response = await Client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}