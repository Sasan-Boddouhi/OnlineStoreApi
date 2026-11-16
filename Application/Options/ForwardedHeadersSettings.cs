namespace Application.Options;

/// <summary>
/// F-20: پروکسی‌های معکوسی که هدرهای X-Forwarded-For و X-Forwarded-Proto آن‌ها پذیرفته می‌شود.
/// فقط آدرس‌هایی که اینجا صریحاً آمده‌اند معتبرند؛ اگر هیچ‌کدام تنظیم نشود، این هدرها کاملاً نادیده
/// گرفته می‌شوند و IP کلاینت همان آدرس اتصال TCP است (پشت پروکسی، یعنی IP خود پروکسی).
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>آدرس IP تک‌تک پروکسی‌های مورد اعتماد، مثل <c>172.18.0.1</c>.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>شبکه‌های مورد اعتماد به‌صورت CIDR، مثل <c>172.18.0.0/16</c>.</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>حداکثر تعداد پروکسی پشت‌سرهم که از X-Forwarded-For خوانده می‌شود (پیش‌فرض ۱).</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>مقادیر خالی (مثلاً متغیر محیطی تنظیم‌نشده در compose) نادیده گرفته می‌شوند.</summary>
    public IEnumerable<string> ConfiguredProxies => NonBlank(KnownProxies);

    public IEnumerable<string> ConfiguredNetworks => NonBlank(KnownNetworks);

    public bool HasTrustedProxies => ConfiguredProxies.Any() || ConfiguredNetworks.Any();

    private static IEnumerable<string> NonBlank(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());
}
