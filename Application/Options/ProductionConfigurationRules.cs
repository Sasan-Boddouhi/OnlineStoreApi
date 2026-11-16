namespace Application.Options;

/// <summary>
/// قوانین ایمنی پیکربندی برای محیط‌های غیر توسعه (F-01, F-14).
/// این کلاس عمداً وابسته به هیچ سرویس میزبانی نیست تا بدون راه‌اندازی host تست شود؛
/// آداپتورهای <c>IValidateOptions</c> در پروژهٔ API همین قوانین را روی ValidateOnStart سوار می‌کنند.
/// </summary>
public static class ProductionConfigurationRules
{
    public const string ProductionEnvironment = "Production";
    public const string DevelopmentEnvironment = "Development";
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// درگاه جعلی فقط در Development و Testing مجاز است.
    /// هر محیط دیگری (Production، Staging، ...) باید درگاه واقعی را انتخاب کند.
    /// </summary>
    public static bool AllowsFakePaymentGateway(string? environmentName) =>
        string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, TestingEnvironment, StringComparison.OrdinalIgnoreCase);

    public static bool IsProduction(string? environmentName) =>
        string.Equals(environmentName, ProductionEnvironment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// خطاهای پیکربندی درگاه پرداخت را برمی‌گرداند؛ فهرست خالی یعنی معتبر.
    /// </summary>
    public static IReadOnlyList<string> ValidatePaymentGateway(
        PaymentGatewaySettings settings,
        string? environmentName)
    {
        var errors = new List<string>();

        // Batch 5: در همهٔ محیط‌ها.
        if (settings.RequestTimeoutSeconds is < 1 or > 120)
            errors.Add("PaymentGatewaySettings:RequestTimeoutSeconds باید بین 1 و 120 باشد.");

        if (settings.VerifyRetryAfterSeconds is < 1 or > 300)
            errors.Add("PaymentGatewaySettings:VerifyRetryAfterSeconds باید بین 1 و 300 باشد.");

        if (settings.OpenPaymentReuseMinutes is < 0 or > 60)
            errors.Add("PaymentGatewaySettings:OpenPaymentReuseMinutes باید بین 0 و 60 باشد.");

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            errors.Add($"PaymentGatewaySettings:BaseUrl باید آدرس مطلق https باشد؛ مقدار فعلی «{settings.BaseUrl}».");

        if (!AllowsFakePaymentGateway(environmentName)
            && !string.Equals(settings.Provider, "Zarinpal", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"PaymentGatewaySettings:Provider در محیط {environmentName} باید «Zarinpal» باشد؛ " +
                $"مقدار فعلی «{settings.Provider}» درگاه جعلی را فعال می‌کند و پرداخت بدون دریافت پول تأیید می‌شود.");
        }

        if (IsProduction(environmentName))
        {
            if (!settings.EnableRealGateway)
                errors.Add("PaymentGatewaySettings:EnableRealGateway باید در Production برابر true باشد.");

            if (string.IsNullOrWhiteSpace(settings.MerchantId))
                errors.Add("PaymentGatewaySettings:MerchantId باید در Production تنظیم شود.");

            if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
                errors.Add("PaymentGatewaySettings:WebhookSecret must be configured in production environment.");

            // Batch 40 (SEC-09): درگاه کاربر را به همین آدرس برمی‌گرداند؛ پیش‌فرض localhost در Production یعنی
            // پرداخت‌کننده به ماشین خودش فرستاده می‌شود و http یعنی Authority در مسیر قابل شنود است.
            if (!Uri.TryCreate(settings.CallbackUrl, UriKind.Absolute, out var callbackUri)
                || callbackUri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"PaymentGatewaySettings:CallbackUrl در Production باید آدرس مطلق https باشد؛ مقدار فعلی «{settings.CallbackUrl}».");
            }
            else if (callbackUri.IsLoopback || string.Equals(callbackUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"PaymentGatewaySettings:CallbackUrl در Production نباید localhost باشد: «{settings.CallbackUrl}».");
            }
        }

        return errors;
    }

    /// <summary>
    /// Batch 40 (SEC-06): هشدارهای <c>AllowedHosts</c>؛ فهرست خالی یعنی مشکلی نیست.
    /// در Production مقدار «*» (یا خالی، که همان «*» است) یعنی هر Host پذیرفته می‌شود؛ راه‌اندازی شکست نمی‌خورد
    /// چون HEALTHCHECK کانتینر با Host=localhost صدا می‌زند و فهرست درست به دامنهٔ استقرار بستگی دارد،
    /// ولی هشدار ثبت می‌شود (مثلاً <c>AllowedHosts=api.example.com;localhost</c>).
    /// </summary>
    public static IReadOnlyList<string> AllowedHostsWarnings(string? allowedHosts, string? environmentName)
    {
        if (!IsProduction(environmentName))
            return Array.Empty<string>();

        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return hosts.Length == 0 || hosts.Contains("*")
            ? new[] { "AllowedHosts در Production روی «*» است و هر Host پذیرفته می‌شود؛ دامنه‌های API را تنظیم کنید (مثلاً AllowedHosts=api.example.com;localhost)." }
            : Array.Empty<string>();
    }

    /// <summary>
    /// در Production باید دست‌کم یک مبدأ CORS واقعی (غیر localhost) تنظیم شده باشد.
    /// در سایر محیط‌ها مقدار پیش‌فرض localhost کد مجاز است.
    /// </summary>
    public static IReadOnlyList<string> ValidateCorsOrigins(
        IReadOnlyCollection<string>? origins,
        string? environmentName)
    {
        var errors = new List<string>();

        if (!IsProduction(environmentName))
            return errors;

        var configured = (origins ?? Array.Empty<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToList();

        if (configured.Count == 0)
        {
            errors.Add("CorsOrigins باید در Production با دامنهٔ فرانت‌اند تنظیم شود (مثلاً CorsOrigins__0=https://shop.example.com).");
            return errors;
        }

        foreach (var origin in configured)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"CorsOrigins شامل مقدار نامعتبر «{origin}» است؛ آدرس مطلق http/https لازم است.");
            }
            else if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"CorsOrigins در Production نباید localhost باشد: «{origin}».");
            }
        }

        return errors;
    }
}
