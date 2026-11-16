using Application.Interfaces;
using BusinessLogic.Services.Implementations;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Batch 40 (SEC-09): در Production آدرس بازگشت باید https و غیر loopback باشد.
        ["PaymentGatewaySettings:CallbackUrl"] = "https://shop.example.com/payment/callback",
        ["CorsOrigins:0"] = "https://shop.example.com",
        // Batch 46: Production needs an HMAC key (the base test factory sets one) and a resend cooldown of at least 60 s
        // (the base test factory sets 0 for ordinary tests).
        ["Otp:ResendCooldownSeconds"] = "120"
    };

    /// <summary>همان factory تست با محیط و تنظیمات دلخواه (پیش‌فرض Production).</summary>
    private sealed class EnvironmentFactory : IntegrationTestFactory<Program>
    {
        private readonly string _environment;
        private readonly Dictionary<string, string?> _settings;
        private readonly Func<AppDbContext, Task>? _prepareDatabase;

        /// <param name="prepareDatabase">
        /// Batch 49b: اگر داده شود، اسکیمای پایگاه داده پیش از همهٔ hosted serviceها ساخته و این تابع اجرا می‌شود
        /// تا بوت‌استرپ انبار پلتفرم وضعیت واقعی پایگاه داده را ببیند.
        /// </param>
        /// <param name="realSmsProvider">
        /// Batch 46: Production refuses to start with the logging SMS sender (N9). Until the real adapter exists (Batch 46b)
        /// these tests stand in for it with the capturing sender marked as a real provider.
        /// </param>
        public EnvironmentFactory(
            string environment,
            Dictionary<string, string?> settings,
            Func<AppDbContext, Task>? prepareDatabase = null,
            bool realSmsProvider = true)
        {
            _environment = environment;
            _settings = settings;
            _prepareDatabase = prepareDatabase;
            Sms.IsRealProvider = realSmsProvider;
        }

        // Batch 5: این تست‌ها درگاهی را می‌سنجند که خودِ برنامه از روی تنظیمات می‌سازد.
        protected override bool UseScriptedPaymentGateway => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));

            if (_prepareDatabase is not null)
            {
                var prepare = _prepareDatabase;
                builder.ConfigureServices(services => services.Insert(0,
                    ServiceDescriptor.Singleton<IHostedService>(sp => new PrepareDatabaseFirst(sp, prepare))));
            }
        }
    }

    /// <summary>نخستین hosted service: اسکیمای SQLite را می‌سازد (پیش از بوت‌استرپ انبار پلتفرم).</summary>
    private sealed class PrepareDatabaseFirst : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly Func<AppDbContext, Task> _prepare;

        public PrepareDatabaseFirst(IServiceProvider services, Func<AppDbContext, Task> prepare)
        {
            _services = services;
            _prepare = prepare;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await _prepare(db);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static readonly Dictionary<string, string?> ValidPlatformWarehouse = new()
    {
        ["PlatformWarehouse:Name"] = "انبار مرکزی",
        ["PlatformWarehouse:ProvinceName"] = "تهران",
        ["PlatformWarehouse:CityName"] = "تهران",
        ["PlatformWarehouse:StreetAddress"] = "خیابان آزادی",
        ["PlatformWarehouse:Plaque"] = "10",
        ["PlatformWarehouse:Unit"] = "1",
        ["PlatformWarehouse:PostalCode"] = "1234567890",
        ["PlatformWarehouse:RecipientFirstName"] = "مسئول",
        ["PlatformWarehouse:RecipientLastName"] = "انبار",
        ["PlatformWarehouse:PhoneNumber"] = "09120001111"
    };

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

    [Theory]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    public void Batch40_Production_WithEmptyJwtIssuerOrAudience_FailsToStart(string key)
    {
        using var factory = new EnvironmentFactory("Production", With((key, "")));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{key}*");
    }

    [Theory]
    [InlineData("https://localhost:5173/payment/callback")]
    [InlineData("https://127.0.0.1/payment/callback")]
    [InlineData("http://shop.example.com/payment/callback")]
    [InlineData("/payment/callback")]
    public void Batch40_Production_WithLoopbackOrInsecureCallbackUrl_FailsToStart(string callbackUrl)
    {
        using var factory = new EnvironmentFactory("Production",
            With(("PaymentGatewaySettings:CallbackUrl", callbackUrl)));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*PaymentGatewaySettings:CallbackUrl*");
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

    // ==================== Batch 49b: انبار پلتفرم ====================

    [Fact]
    public void Batch49b_Production_WithoutPlatformWarehouseAndWithoutConfiguration_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production", With(), prepareDatabase: _ => Task.CompletedTask);

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*PlatformWarehouse*");
    }

    [Fact]
    public void Batch49b_Production_WithoutPlatformWarehouseAndWithInvalidConfiguration_FailsToStart()
    {
        var settings = With(ValidPlatformWarehouse.Select(kv => (kv.Key, kv.Value)).ToArray());
        settings["PlatformWarehouse:PhoneNumber"] = "12345";

        using var factory = new EnvironmentFactory("Production", settings, prepareDatabase: _ => Task.CompletedTask);

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*PlatformWarehouse:PhoneNumber*");
    }

    [Fact]
    public async Task Batch49b_Production_WithoutPlatformWarehouse_WithConfiguration_StartsAndCreatesIt()
    {
        var settings = With(ValidPlatformWarehouse.Select(kv => (kv.Key, kv.Value)).ToArray());
        using var factory = new EnvironmentFactory("Production", settings, prepareDatabase: _ => Task.CompletedTask);

        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var platform = await db.Warehouse.AsNoTracking().Where(w => w.SellerId == null).ToListAsync();
        platform.Should().ContainSingle().Which.Name.Should().Be("انبار مرکزی");
    }

    // ==================== Batch 46: OTP / SMS ====================

    [Fact]
    public void Batch46_Production_WithOnlyTheLoggingSmsSender_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production", With(), realSmsProvider: false);

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Sms:Provider*");
    }

    [Fact]
    public void Batch46_Production_WithTheRealLoggingSenderRegistration_FailsToStart()
    {
        // no test stand-in: the application's own registration (LoggingSmsSender) must stop Production.
        using var factory = new LoggingSenderFactory("Production", With());

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Sms:Provider*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Batch46_Production_WithoutAStrongOtpHmacKey_FailsToStart(string key)
    {
        using var factory = new EnvironmentFactory("Production", With(("Otp:HmacKey", key)));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Otp:HmacKey*");
    }

    [Fact]
    public void Batch46_Production_WithAResendCooldownBelow60Seconds_FailsToStart()
    {
        using var factory = new EnvironmentFactory("Production", With(("Otp:ResendCooldownSeconds", "10")));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Otp:ResendCooldownSeconds*");
    }

    [Fact]
    public void Batch46_AnSmsProviderWithoutAnAdapter_FailsToStart_InEveryEnvironment()
    {
        using var factory = new EnvironmentFactory("Testing", new Dictionary<string, string?> { ["Sms:Provider"] = "Kavenegar" });

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Sms:Provider*");
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public void Batch46_DevelopmentAndTesting_StartWithTheLoggingSender_AndWithoutAnHmacKey(string environment)
    {
        using var factory = new LoggingSenderFactory(environment, new Dictionary<string, string?> { ["Otp:HmacKey"] = "" });

        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<Application.Interfaces.ISmsSender>()
            .Should().BeOfType<LoggingSmsSender>();
    }

    /// <summary>Batch 46: keeps the application's own ISmsSender registration (no capturing stand-in).</summary>
    private sealed class LoggingSenderFactory : IntegrationTestFactory<Program>
    {
        private readonly string _environment;
        private readonly Dictionary<string, string?> _settings;

        public LoggingSenderFactory(string environment, Dictionary<string, string?> settings)
        {
            _environment = environment;
            _settings = settings;
        }

        protected override bool UseScriptedPaymentGateway => false;

        protected override bool UseCapturingSmsSender => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        }
    }

    [Fact]
    public void Batch49b_Production_WithExistingPlatformWarehouse_WithoutConfiguration_Starts()
    {
        // Seed تست یک انبار پلتفرم می‌سازد؛ پیکربندی فقط وقتی لازم است که انباری نباشد.
        using var factory = new EnvironmentFactory("Production", With(),
            prepareDatabase: db => TestDataSeed.SeedAsync(db));

        var act = () => factory.CreateClient().Dispose();

        act.Should().NotThrow();
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
