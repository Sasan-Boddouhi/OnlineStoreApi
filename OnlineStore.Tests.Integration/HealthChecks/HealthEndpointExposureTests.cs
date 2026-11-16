using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.HealthChecks;

/// <summary>
/// Batch 52 (OPS-07): خروجی تفصیلی <c>/health</c> (نام وابستگی‌ها، مدت، توضیح) بیرون از Development
/// برای کاربر ناشناس بسته است و فقط مدیر (سیاست AdminOnly) آن را می‌بیند.
/// <c>/health/live</c> و <c>/health/ready</c> عمومی و حداقلی می‌مانند (بدون نام وابستگی).
/// </summary>
public sealed class HealthEndpointExposureTests : ControllerIntegrationTestBase
{
    public HealthEndpointExposureTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task DetailedHealth_Anonymous_OutsideDevelopment_IsUnauthorized()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sqlserver").And.NotContain("redis");
    }

    [Fact]
    public async Task DetailedHealth_NonAdminUser_OutsideDevelopment_IsForbidden()
    {
        var token = await RegisterCustomerAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DetailedHealth_Admin_OutsideDevelopment_SeesTheDependencyChecks()
    {
        var token = await GetAdminTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        // در Testing اتصال SQL Server واقعی وجود ندارد؛ وضعیت 200 یا 503 است ولی جزئیات برمی‌گردد.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"checks\"").And.Contain("sqlserver");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task LivenessAndReadiness_StayAnonymousAndMinimal(string path)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sqlserver").And.NotContain("redis");
    }

    [Fact]
    public async Task Production_DetailedHealthIsClosed_LivenessAndReadinessStayOpen()
    {
        using var factory = new EnvironmentFactory("Production", ValidProduction);
        using var client = factory.CreateClient();

        var detailed = await client.GetAsync("/health");
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        detailed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await detailed.Content.ReadAsStringAsync()).Should().NotContain("sqlserver");
        live.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
        ready.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
        (await ready.Content.ReadAsStringAsync()).Should().NotContain("sqlserver");
    }

    [Fact]
    public async Task Development_DetailedHealthStaysAnonymous()
    {
        using var factory = new EnvironmentFactory("Development", new Dictionary<string, string?>());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"checks\"");
    }

    private async Task<string> RegisterCustomerAsync()
    {
        var registerData = new
        {
            PhoneNumber = $"0912{Random.Shared.Next(0, 10_000_000):D7}",
            Password = "Customer@123",
            FirstName = "Customer",
            LastName = "Health",
            DeviceId = "batch52-health-device",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, registerData);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!.AccessToken;
    }

    private static readonly Dictionary<string, string?> ValidProduction = new()
    {
        ["PaymentGatewaySettings:Provider"] = "Zarinpal",
        ["PaymentGatewaySettings:EnableRealGateway"] = "true",
        ["PaymentGatewaySettings:MerchantId"] = "00000000-0000-0000-0000-000000000000",
        ["PaymentGatewaySettings:WebhookSecret"] = "prod-webhook-secret-for-tests",
        ["PaymentGatewaySettings:CallbackUrl"] = "https://shop.example.com/payment/callback",
        ["CorsOrigins:0"] = "https://shop.example.com",
        // Batch 46: Production needs a resend cooldown of at least 60 s (the base test factory sets 0).
        ["Otp:ResendCooldownSeconds"] = "120"
    };

    /// <summary>همان factory تست با محیط و تنظیمات دلخواه.</summary>
    private sealed class EnvironmentFactory : IntegrationTestFactory<Program>
    {
        private readonly string _environment;
        private readonly Dictionary<string, string?> _settings;

        public EnvironmentFactory(string environment, Dictionary<string, string?> settings)
        {
            _environment = environment;
            _settings = settings;
            // Batch 46: Production refuses the logging SMS sender (N9); the capturing sender stands in for the
            // real provider adapter (Batch 46b) so this test can start a Production host.
            Sms.IsRealProvider = true;
        }

        protected override bool UseScriptedPaymentGateway => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        }
    }
}
