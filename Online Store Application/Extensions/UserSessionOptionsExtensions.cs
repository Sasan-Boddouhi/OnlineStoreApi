using Application.Options;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>
/// Batch 11 (F-13): قیدهای بین سیاست نشست و طول عمر access token.
/// اگر بی‌کاری کوتاه‌تر از عمر access token باشد، نخستین refresh پس از انقضای access token همیشه
/// نشست را منقضی می‌بیند و کاربر بیرون می‌افتد (همان خطای F-13)؛ پس راه‌اندازی شکست می‌خورد.
/// </summary>
public sealed class UserSessionOptionsValidator : IValidateOptions<UserSessionOptions>
{
    private readonly IOptions<JwtOptions> _jwtOptions;

    public UserSessionOptionsValidator(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions;
    }

    public ValidateOptionsResult Validate(string? name, UserSessionOptions options)
    {
        var errors = new List<string>();
        var accessTokenLifetime = TimeSpan.FromMinutes(_jwtOptions.Value.ExpireMinutes);

        if (options.IdleTimeout <= accessTokenLifetime)
            errors.Add($"Session:IdleTimeoutDays ({options.IdleTimeout}) باید بیشتر از عمر access token یعنی Jwt:ExpireMinutes ({accessTokenLifetime}) باشد.");

        if (options.AbsoluteLifetime < options.IdleTimeout)
            errors.Add("Session:AbsoluteLifetimeDays نباید کمتر از Session:IdleTimeoutDays باشد.");

        if (options.RefreshReuseGracePeriod >= accessTokenLifetime)
            errors.Add("Session:RefreshReuseGraceSeconds باید کمتر از عمر access token باشد.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class UserSessionOptionsExtensions
{
    /// <summary>JwtOptions و UserSessionOptions را با ValidateOnStart ثبت می‌کند تا تنظیم نادرست هنگام راه‌اندازی شکست بخورد.</summary>
    public static IServiceCollection AddUserSessionOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<UserSessionOptions>()
            .Bind(configuration.GetSection(UserSessionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<UserSessionOptions>, UserSessionOptionsValidator>();

        return services;
    }
}
