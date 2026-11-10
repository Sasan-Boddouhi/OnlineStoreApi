using Application.Interfaces;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// F-01/F-14 — در Production با درگاه جعلی یا CORS ناقص، راه‌اندازی باید شکست بخورد
/// (ValidateOnStart)، و در Development/Testing همچنان کار کند.
/// </summary>
public class ProductionStartupValidationTests
{
    private static readonly Dictionary<string, string?> ValidProduction = new()
    {
        ["PaymentGatewaySettings:Provider"] = "Zarinpal",
        ["PaymentGatewaySettings:EnableRealGateway"] = "true",
        ["PaymentGatewaySettings:MerchantId"] = "00000000-0000-0000-0000-000000000000",
        ["PaymentGatewaySettings:WebhookSecret"] = "prod-webhook-secret-for-tests",
        ["CorsOrigins:0"] = "https://shop.example.com"
    };

    /// <summary>همان factory تست با محیط و تنظیمات دلخواه (پیش‌فرض Production).</summary>
    private sealed class EnvironmentFactory : IntegrationTestFactory<Program>
    {
        private readonly string _environment;
        private readonly Dictionary<string, string?> _settings;

        public EnvironmentFactory(string environment, Dictionary<string, string?> settings)
        {
            _environment = environment;
            _settings = settings;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        }
    }

    private static Dictionary<string, string?> With(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>(ValidProduction);
        foreach (var (key, value) in overrides) settings[key] = value;
        return settings;
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("")]
    [InlineData("IDPay")]
    public void Production_WithFakeOrManualProvider_FailsToStart(string provider)
    {
        using var factory = new EnvironmentFactory("Production",
            With(("PaymentGatewaySettings:Provider", provider)));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*PaymentGatewaySettings:Provider*");
    }

    [Fact]
    public void Production_WithRealGatewayDisabled_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production",
            With(("PaymentGatewaySettings:EnableRealGateway", "false")));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*EnableRealGateway*");
    }

    [Fact]
    public void Production_WithoutMerchantId_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production",
            With(("PaymentGatewaySettings:MerchantId", "")));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MerchantId*");
    }

    [Fact]
    public void Production_WithoutCorsOrigins_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production",
            With(("CorsOrigins:0", null)));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*CorsOrigins*");
    }

    [Fact]
    public void Production_WithFullZarinpalSettings_StartsAndUsesZarinpal()
    {
        using var factory = new EnvironmentFactory("Production", With());

        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPaymentGateway>()
            .Should().BeOfType<ZarinpalPaymentGateway>();
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public void DevelopmentAndTesting_WithDefaultManualProvider_StillStartWithFakeGateway(string environment)
    {
        // بدون هیچ تنظیم پرداخت/CORS؛ پیش‌فرض Manual.
        using var factory = new EnvironmentFactory(environment, new Dictionary<string, string?>());

        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPaymentGateway>()
            .Should().BeOfType<FakePaymentGateway>();
    }

    [Fact]
    public void Staging_WithManualProvider_FailsToStart_AndFakeGatewayIsNotResolvable()
    {
        using var factory = new EnvironmentFactory("Staging",
            new Dictionary<string, string?> { ["PaymentGatewaySettings:Provider"] = "Manual" });

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*PaymentGatewaySettings:Provider*");
    }
}
