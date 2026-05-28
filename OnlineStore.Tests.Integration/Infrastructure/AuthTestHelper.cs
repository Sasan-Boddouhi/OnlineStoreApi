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
            PhoneNumber = "admin@test.com",
            Password = "123456",
            DeviceId = "test"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", login);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();

        return result!.AccessToken;
    }
}