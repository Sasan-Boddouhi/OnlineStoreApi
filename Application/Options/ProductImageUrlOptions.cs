namespace Application.Options;

/// <summary>
/// Batch 48c (SEC-12) — فهرست میزبان‌های مجاز برای ثبت تصویر محصول از طریق آدرس خارجی.
///
/// سرور آدرس را فقط ذخیره می‌کند و هرگز نمی‌خواند، اما فرانت‌اند همین آدرس را در ویترین نمایش می‌دهد؛
/// پس بدون این فهرست هر میزبان دلخواه می‌تواند در ویترین تزریق شود. تصویرهای آپلودشده (آدرس محلی
/// <see cref="StorageSettings.PublicBaseUrl"/>) مشمول این فهرست نیستند.
///
/// فهرست خالی (پیش‌فرض امن) یعنی ثبت تصویر با آدرس خارجی خاموش است و هر آدرس خارجی رد می‌شود.
/// هر عضو فقط نام میزبان است: بدون پروتکل، مسیر، پورت، فاصله یا wildcard.
/// </summary>
public sealed class ProductImageUrlOptions
{
    public const string SectionName = "ProductImages";

    /// <summary>نام میزبان‌های مجاز (مثلاً <c>cdn.example.com</c>). مقایسه بدون حساسیت به حروف و دقیق است.</summary>
    public string[] AllowedExternalHosts { get; set; } = [];

    /// <summary>پیام خطای هر عضو نامعتبر را برمی‌گرداند؛ فهرست خالی یعنی همه معتبرند.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var hosts = AllowedExternalHosts ?? [];

        for (var i = 0; i < hosts.Length; i++)
        {
            if (!IsValidHost(hosts[i]))
                errors.Add(
                    $"{SectionName}:{nameof(AllowedExternalHosts)}:{i} نامعتبر است. " +
                    "هر عضو باید فقط نام میزبان باشد (مثلاً cdn.example.com) بدون پروتکل، مسیر، پورت، فاصله یا wildcard.");
        }

        return errors;
    }

    private static readonly string ForbiddenChars = "*/" + (char)92 + ":@?#";

    /// <summary>نام میزبان DNS معتبر و بدون هرگونه جزء اضافه.</summary>
    public static bool IsValidHost(string? host)
        => !string.IsNullOrEmpty(host)
           && host.All(c => !char.IsWhiteSpace(c) && ForbiddenChars.IndexOf(c) < 0)
           && Uri.CheckHostName(host) == UriHostNameType.Dns;
}
