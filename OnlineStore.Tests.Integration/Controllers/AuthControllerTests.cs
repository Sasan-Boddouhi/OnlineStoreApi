using System.Net;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Auth;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Controllers;

[Collection("DatabaseCollection")]
public class AuthControllerIntegrationTests : BaseIntegrationTest
{
    public AuthControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Register_Login_Refresh_Logout_FullFlow_Success()
    {
        var unique = Random.Shared.Next(100000000, 999999999);

        var registerDto = new RegisterDto
        {
            FirstName = "Flow",
            LastName = "Test",
            PhoneNumber = $"09{unique}",
            Password = "Password123!",
            DateOfBirth = "1370/01/01",
            DeviceId = Guid.NewGuid().ToString(),
            DeviceName = "IntegrationTestRunner",
            Email = $"testuser_{Guid.NewGuid():N}@example.com"
        };

        var registerResponse = await Client.PostAsJsonAsync("/api/auth/register", registerDto);

        if (registerResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            var errors = await registerResponse.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Registration validation failed. Server responded: {errors}");
        }

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var registerResult = await registerResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        registerResult.Should().NotBeNull();
        var refreshToken = registerResult!.RefreshToken;

        // 2. Login - Match password changes above
        var loginDto = new LoginDto
        {
            PhoneNumber = registerDto.PhoneNumber,
            Password = "Password123!",
            DeviceId = registerDto.DeviceId
        };
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        loginResult.Should().NotBeNull();
        var accessToken = loginResult!.AccessToken;

        // 3. Refresh
        var refreshDto = new RefreshTokenDto { RefreshToken = refreshToken };
        var refreshResponse = await Client.PostAsJsonAsync("/api/auth/refresh", refreshDto);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        refreshResult.Should().NotBeNull();

        // 4. Logout
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var logoutResponse = await Client.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. After logout, token should be invalid
        var meResponse = await Client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_LockoutAfter5FailedAttempts_ReturnsUnauthorized()
    {
        var phone = "09129999988";
        var registerDto = new RegisterDto
        {
            FirstName = "Lockout",
            LastName = "Test",
            PhoneNumber = phone,
            Password = "correct",
            DateOfBirth = "1370/01/01"
        };
        await Client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = new LoginDto { PhoneNumber = phone, Password = "wrong", DeviceId = "device" };
        for (int i = 0; i < 5; i++)
        {
            var response = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        // ششمین تلاش – حتی با رمز صحیح
        loginDto.Password = "correct";
        var finalResponse = await Client.PostAsJsonAsync("/api/auth/login", loginDto);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}