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
        WebhookSecret = "secret"
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

        errors.Should().HaveCount(4); // Provider, EnableRealGateway, MerchantId, WebhookSecret
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
    public void JwtTokenService_MissingExpireMinutes_FallsBackToSixtyMinutes()
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

        jwt.ValidTo.Should().BeCloseTo(before.AddMinutes(60), TimeSpan.FromSeconds(5));
    }
}
