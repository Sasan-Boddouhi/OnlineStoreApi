using System.Security.Cryptography;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.Services.Implementations;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>
/// Batch 46: اعتبارسنجی <c>Otp:*</c> هنگام راه‌اندازی. در Production کلید HMAC و یک فرستندهٔ واقعی پیامک لازم است
/// (فرستندهٔ لاگ‌کننده رد می‌شود، N9)؛ در همهٔ محیط‌ها سقف‌ها باید معقول باشند.
/// </summary>
public sealed class OtpOptionsValidator : IValidateOptions<OtpOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ISmsSender _smsSender;

    public OtpOptionsValidator(IHostEnvironment environment, ISmsSender smsSender)
    {
        _environment = environment;
        _smsSender = smsSender;
    }

    public ValidateOptionsResult Validate(string? name, OtpOptions options)
    {
        var errors = options.Validate(_environment.EnvironmentName, _smsSender.IsRealProvider);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>Batch 46: <c>Sms:Provider</c> فقط نامی که آداپتور دارد؛ فعلاً فقط <c>Logging</c> (آداپتور واقعی: Batch 46b).</summary>
public sealed class SmsOptionsValidator : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options) =>
        string.IsNullOrWhiteSpace(options.Provider)
        || string.Equals(options.Provider.Trim(), SmsOptions.LoggingProvider, StringComparison.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Sms:Provider «{options.Provider}» آداپتور ندارد؛ فعلاً فقط «{SmsOptions.LoggingProvider}» پشتیبانی می‌شود (آداپتور واقعی: Batch 46b).");
}

public static class PhoneVerificationExtensions
{
    /// <summary>
    /// تنظیمات OTP و فرستندهٔ پیامک را ثبت می‌کند. بیرون از Production اگر <c>Otp:HmacKey</c> خالی باشد کلید تصادفی همین
    /// فرایند ساخته می‌شود (کدهای صادرشده با راه‌اندازی دوباره باطل می‌شوند)؛ در Production خالی بودن آن راه‌اندازی را متوقف می‌کند.
    /// </summary>
    public static IServiceCollection AddPhoneVerification(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmsOptions>, SmsOptionsValidator>();

        // تنها آداپتور این دور؛ در Production با OtpOptionsValidator رد می‌شود.
        services.AddSingleton<ISmsSender, LoggingSmsSender>();

        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, hostEnvironment) =>
            {
                if (string.IsNullOrEmpty(options.HmacKey)
                    && !ProductionConfigurationRules.IsProduction(hostEnvironment.EnvironmentName))
                {
                    options.HmacKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
                }
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OtpOptions>, OtpOptionsValidator>();

        return services;
    }
}
