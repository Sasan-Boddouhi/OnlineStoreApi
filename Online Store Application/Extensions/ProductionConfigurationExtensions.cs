using Application.Options;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>مبدأهای CORS برای اعتبارسنجی هنگام راه‌اندازی (کلید ریشهٔ <c>CorsOrigins</c>).</summary>
public sealed class CorsOriginsOptions
{
    public string[]? Origins { get; set; }
}

/// <summary>اعتبارسنجی درگاه پرداخت هنگام راه‌اندازی؛ درگاه جعلی در Production ممنوع است (F-01).</summary>
public sealed class PaymentGatewaySettingsValidator : IValidateOptions<PaymentGatewaySettings>
{
    private readonly IHostEnvironment _environment;

    public PaymentGatewaySettingsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, PaymentGatewaySettings options)
    {
        var errors = ProductionConfigurationRules.ValidatePaymentGateway(options, _environment.EnvironmentName);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>در Production مبدأ CORS باید صریحاً تنظیم شده باشد (F-14).</summary>
public sealed class CorsOriginsOptionsValidator : IValidateOptions<CorsOriginsOptions>
{
    private readonly IHostEnvironment _environment;

    public CorsOriginsOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, CorsOriginsOptions options)
    {
        var errors = ProductionConfigurationRules.ValidateCorsOrigins(options.Origins, _environment.EnvironmentName);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class ProductionConfigurationExtensions
{
    /// <summary>
    /// تنظیمات پرداخت و CORS را با ValidateOnStart ثبت می‌کند و درگاه را فقط به‌صورت امن می‌سازد:
    /// <see cref="BusinessLogic.Services.Implementations.FakePaymentGateway"/> بیرون از Development/Testing
    /// هرگز ساخته نمی‌شود.
    /// </summary>
    public static IServiceCollection AddProductionSafeConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PaymentGatewaySettings>()
            .Bind(configuration.GetSection(PaymentGatewaySettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaymentGatewaySettings>, PaymentGatewaySettingsValidator>();

        services.AddOptions<CorsOriginsOptions>()
            .Configure(o => o.Origins = configuration.GetSection("CorsOrigins").Get<string[]>())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsOriginsOptions>, CorsOriginsOptionsValidator>();

        return services;
    }

    /// <summary>انتخاب درگاه در زمان اجرا؛ درگاه جعلی فقط در Development/Testing قابل حل است.</summary>
    public static PaymentGatewayKind ResolveGatewayKind(string? provider, string? environmentName) =>
        string.Equals(provider, "Zarinpal", StringComparison.OrdinalIgnoreCase)
            ? PaymentGatewayKind.Zarinpal
            : ProductionConfigurationRules.AllowsFakePaymentGateway(environmentName)
                ? PaymentGatewayKind.Fake
                : throw new InvalidOperationException(
                    $"درگاه پرداخت «{provider}» در محیط {environmentName} مجاز نیست؛ فقط Zarinpal.");
}

/// <summary>نوع درگاهی که باید ساخته شود.</summary>
public enum PaymentGatewayKind
{
    Zarinpal,
    Fake
}
