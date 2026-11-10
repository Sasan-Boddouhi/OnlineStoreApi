using System.Net;
using Application.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>
/// F-20: اعتبارسنجی تنظیمات پروکسی هنگام راه‌اندازی (ValidateOnStart).
/// شبکه‌ای با طول پیشوند صفر (مثل <c>0.0.0.0/0</c>) رد می‌شود، چون یعنی «به همه اعتماد کن»
/// و هر کلاینتی با جعل X-Forwarded-For برای هر درخواست سهمیهٔ تازه می‌گرفت.
/// </summary>
public sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        var errors = new List<string>();

        if (options.ForwardLimit is < 1 or > 10)
            errors.Add("ForwardedHeaders:ForwardLimit باید بین ۱ و ۱۰ باشد.");

        foreach (var proxy in options.ConfiguredProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
                errors.Add($"ForwardedHeaders:KnownProxies — «{proxy}» آدرس IP معتبر نیست.");
        }

        foreach (var network in options.ConfiguredNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
                errors.Add($"ForwardedHeaders:KnownNetworks — «{network}» شبکهٔ CIDR معتبر نیست (مثال: 172.18.0.0/16؛ بیت‌های میزبان باید صفر باشند).");
            else if (parsed.PrefixLength == 0)
                errors.Add($"ForwardedHeaders:KnownNetworks — «{network}» یعنی اعتماد به همهٔ آدرس‌ها و مجاز نیست.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// ForwardedHeaders را فقط برای پروکسی‌های صریحاً تنظیم‌شده فعال می‌کند. پیش‌فرض‌های فریم‌ورک
    /// (loopback) پاک می‌شوند تا فقط تنظیمات معتبر باشد؛ و اگر هیچ پروکسی‌ای تنظیم نشده باشد،
    /// پردازش هدرها کلاً خاموش می‌شود. (فهرست خالی در ForwardedHeadersMiddleware یعنی اعتماد به همه،
    /// پس هرگز با فهرست خالی فعال نمی‌شود.)
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) => Apply(options, settings.Value));

        return services;
    }

    /// <summary>
    /// باید اولین middleware باشد تا محدودیت نرخ، احراز هویت، لاگ‌ها و نشست‌ها IP واقعی کلاینت را ببینند.
    /// در Production اگر هیچ پروکسی مورد اعتمادی تنظیم نشده باشد، هشدار راه‌اندازی ثبت می‌شود.
    /// </summary>
    public static WebApplication UseTrustedForwardedHeaders(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;

        if (app.Environment.IsProduction() && !settings.HasTrustedProxies)
        {
            app.Logger.LogWarning(
                "هیچ پروکسی مورد اعتمادی در ForwardedHeaders:KnownProxies یا ForwardedHeaders:KnownNetworks تنظیم نشده است؛ " +
                "X-Forwarded-For نادیده گرفته می‌شود. اگر API پشت nginx/Docker است، همهٔ کلاینت‌ها IP پروکسی را می‌گیرند " +
                "و محدودیت نرخ ورود/ثبت‌نام بین همهٔ کاربران مشترک می‌شود. docs/deployment.md را ببینید.");
        }

        app.UseForwardedHeaders();
        return app;
    }

    private static void Apply(ForwardedHeadersOptions options, ForwardedHeadersSettings settings)
    {
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        if (!settings.HasTrustedProxies)
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = settings.ForwardLimit;

        foreach (var proxy in settings.ConfiguredProxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));

        foreach (var network in settings.ConfiguredNetworks)
        {
            var parsed = System.Net.IPNetwork.Parse(network);
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(parsed.BaseAddress, parsed.PrefixLength));
        }
    }
}
