using OnlineStore.Tests.Integration.Infrastructure;
using System.Net.Http.Json;

namespace OnlineStore.Tests.Integration.Fixtures;

public abstract class ControllerIntegrationTestBase : BaseIntegrationTest
{
    protected ControllerIntegrationTestBase(IntegrationTestFactory<Program> factory) : base(factory) { }

    protected async Task<string> GetAdminTokenAsync()
    {
        var loginData = new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "test-device"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", loginData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return result!.AccessToken;
    }

    private class TokenResponse
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
    }
}