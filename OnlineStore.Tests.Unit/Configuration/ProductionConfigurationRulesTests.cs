using Application.Options;
using FluentAssertions;
using Online_Store_Application.Extensions;

namespace OnlineStore.Tests.Unit.Configuration;

/// <summary>F-01/F-14 — قوانین پیکربندی Production به‌صورت واحد.</summary>
public class ProductionConfigurationRulesTests
{
    private static PaymentGatewaySettings ValidZarinpal() => new()
    {
        Provider = "Zarinpal",
        EnableRealGateway = true,
        MerchantId = "merchant",
        WebhookSecret = "secret",
        CallbackUrl = "https://shop.example.com/payment/callback"
    };

    [Fact]
    public void Production_FakeProvider_IsRejected()
    {
        var settings = ValidZarinpal();
        settings.Provider = "Manual";

        ProductionConfigurationRules.ValidatePaymentGateway(settings, "Production")
            .Should().ContainSingle(e => e.Contains("PaymentGatewaySettings:Provider"));
    }

    [Fact]
    public void Production_FullZarinpal_IsValid()
    {
        ProductionConfigurationRules.ValidatePaymentGateway(ValidZarinpal(), "Production")
            .Should().BeEmpty();
    }

    [Fact]
    public void Production_ReportsEveryMissingSetting()
    {
        var errors = ProductionConfigurationRules.ValidatePaymentGateway(new PaymentGatewaySettings(), "Production");

        // Batch 40: پیش‌فرض CallbackUrl (localhost) هم در Production رد می‌شود.
        errors.Should().HaveCount(5); // Provider, EnableRealGateway, MerchantId, WebhookSecret, CallbackUrl
    }

    [Theory]
    [InlineData("https://localhost:5173/payment/callback")]
    [InlineData("https://127.0.0.1/payment/callback")]
    [InlineData("https://[::1]/payment/callback")]
    [InlineData("http://shop.example.com/payment/callback")]
    [InlineData("/payment/callback")]
    [InlineData("")]
    public void Batch40_Production_LoopbackInsecureOrRelativeCallbackUrl_IsRejected(string callbackUrl)
    {
        var settings = ValidZarinpal();
        settings.CallbackUrl = callbackUrl;

        ProductionConfigurationRules.ValidatePaymentGateway(settings, "Production")
            .Should().ContainSingle(e => e.Contains("PaymentGatewaySettings:CallbackUrl"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void Batch40_OutsideProduction_LocalCallbackUrl_IsAllowed(string environment)
    {
        var settings = ValidZarinpal();
        settings.CallbackUrl = "http://localhost:5173/payment/callback";

        ProductionConfigurationRules.ValidatePaymentGateway(settings, environment)
            .Should().NotContain(e => e.Contains("PaymentGatewaySettings:CallbackUrl"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("api.example.com;*")]
    public void Batch40_Production_WildcardOrMissingAllowedHosts_IsReported(string? allowedHosts)
    {
        ProductionConfigurationRules.AllowedHostsWarnings(allowedHosts, "Production")
            .Should().ContainSingle(w => w.Contains("AllowedHosts"));
    }

    [Fact]
    public void Batch40_AllowedHosts_ConfiguredInProduction_OrAnyValueElsewhere_IsNotReported()
    {
        ProductionConfigurationRules.AllowedHostsWarnings("api.example.com;localhost", "Production").Should().BeEmpty();
        ProductionConfigurationRules.AllowedHostsWarnings("*", "Development").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Batch05_OutOfRangeTimingSettings_AreRejected_InEveryEnvironment(string environment)
    {
        var settings = ValidZarinpal();
        settings.RequestTimeoutSeconds = 0;
        settings.VerifyRetryAfterSeconds = 0;
        settings.OpenPaymentReuseMinutes = -1;
        settings.BaseUrl = "http://payment.zarinpal.com";

        ProductionConfigurationRules.ValidatePaymentGateway(settings, environment)
            .Should().HaveCount(4);
    }

    [Fact]
    public void Batch05_Defaults_PointAtTheDocumentedZarinpalHost_AndAreValid()
    {
        var settings = new PaymentGatewaySettings();

        settings.BaseUrl.Should().Be("https://payment.zarinpal.com");
        settings.VerifyRetryAfterSeconds.Should().BeInRange(1, 300);
        ProductionConfigurationRules.ValidatePaymentGateway(settings, "Development").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void DevelopmentAndTesting_DefaultSettings_AreValid(string environment)
    {
        ProductionConfigurationRules.ValidatePaymentGateway(new PaymentGatewaySettings(), environment)
            .Should().BeEmpty();
        ProductionConfigurationRules.AllowsFakePaymentGateway(environment).Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void OtherEnvironments_DoNotAllowFakeGateway(string environment)
    {
        ProductionConfigurationRules.AllowsFakePaymentGateway(environment).Should().BeFalse();
    }

    [Fact]
    public void Staging_ZarinpalSandboxWithoutRealGatewayFlag_IsValid()
    {
        var settings = new PaymentGatewaySettings { Provider = "Zarinpal", UseSandbox = true };

        ProductionConfigurationRules.ValidatePaymentGateway(settings, "Staging").Should().BeEmpty();
    }

    [Fact]
    public void ResolveGatewayKind_FakeOutsideDevelopmentAndTesting_Throws()
    {
        var act = () => ProductionConfigurationExtensions.ResolveGatewayKind("Manual", "Production");

        act.Should().Throw<InvalidOperationException>();
        ProductionConfigurationExtensions.ResolveGatewayKind("Manual", "Development").Should().Be(PaymentGatewayKind.Fake);
        ProductionConfigurationExtensions.ResolveGatewayKind("zarinpal", "Production").Should().Be(PaymentGatewayKind.Zarinpal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Production_NoCorsOrigins_IsRejected(string? origin)
    {
        var origins = origin is null ? null : new[] { origin };

        ProductionConfigurationRules.ValidateCorsOrigins(origins, "Production")
            .Should().ContainSingle(e => e.Contains("CorsOrigins"));
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("https://127.0.0.1:3000")]
    [InlineData("shop.example.com")]
    public void Production_LocalhostOrMalformedOrigin_IsRejected(string origin)
    {
        ProductionConfigurationRules.ValidateCorsOrigins(new[] { origin }, "Production")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Production_RealOrigin_IsValid_AndOtherEnvironmentsAreNotChecked()
    {
        ProductionConfigurationRules.ValidateCorsOrigins(new[] { "https://shop.example.com" }, "Production")
            .Should().BeEmpty();
        ProductionConfigurationRules.ValidateCorsOrigins(null, "Development").Should().BeEmpty();
    }

    [Fact]
    public void JwtTokenService_MissingExpireMinutes_FallsBackToFifteenMinutes()
    {
        // ExpireMinutes در تنظیمات نیامده؛ مقدار پیش‌فرض JwtOptions باید اعمال شود (قبلاً int.Parse(null) می‌ترکید).
        var options = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Key = new string('k', 40),
            Issuer = "issuer",
            Audience = "audience"
        });
        var service = new JwtTokenService(options);

        var before = DateTime.UtcNow;
        var token = service.GenerateToken(new List<System.Security.Claims.Claim>());
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

        // Batch 11: پیش‌فرض access token از ۶۰ به ۱۵ دقیقه کاهش یافت (نشست با refresh زنده می‌ماند).
        jwt.ValidTo.Should().BeCloseTo(before.AddMinutes(15), TimeSpan.FromSeconds(5));
    }
}
