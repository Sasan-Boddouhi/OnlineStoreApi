namespace Application.Options;

/// <summary>
/// F-04: تنظیمات کش خروجی HTTP.
/// کش خروجی فقط روی endpointهایی اعمال می‌شود که صراحتاً یک سیاست نام‌دار دارند؛
/// سیاست پایهٔ سراسری وجود ندارد.
/// </summary>
public sealed class OutputCachingOptions
{
    public const string SectionName = "OutputCaching";

    /// <summary>
    /// روشن/خاموش بودن کش خروجی. اگر مقدار ندهید، در Testing خاموش و در بقیهٔ محیط‌ها روشن است.
    /// تست‌های جداسازی کش آن را صراحتاً روشن می‌کنند تا رفتار واقعی pipeline سنجیده شود.
    /// </summary>
    public bool? Enabled { get; set; }
}
