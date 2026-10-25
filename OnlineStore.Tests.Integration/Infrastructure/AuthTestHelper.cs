using System.Net.Http.Json;
using BusinessLogic.DTOs.Auth;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class AuthTestHelper
{
    private readonly HttpClient _client;

    public AuthTestHelper(HttpClient client)
    {
        _client = client;
    }

    public async Task<string> GetAdminTokenAsync()
    {
        var login = new LoginDto
        {
            PhoneNumber = "09120000000",   // شماره موبایل ادمین در SeedData
            Password = "Admin@123",        // رمز عبور واقعی
            DeviceId = "test"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", login);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();

        return result!.AccessToken;
    }
}