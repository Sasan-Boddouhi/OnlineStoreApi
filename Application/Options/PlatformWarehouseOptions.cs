using Application.Common.Helpers;

namespace Application.Options;

/// <summary>
/// Batch 49b — مشخصات انبار پلتفرم (انبار با <c>SellerId == null</c>) که هنگام راه‌اندازی، اگر هیچ انبار
/// پلتفرمی وجود نداشته باشد، از روی همین تنظیمات ساخته می‌شود (<c>PlatformWarehouseBootstrapper</c>).
/// <para>
/// پیش‌فرض‌های کد امن‌اند: فقط <see cref="Name"/> مقدار دارد و همهٔ فیلدهای نشانی خالی‌اند، پس بدون پیکربندی
/// هیچ نشانی ساختگی ساخته نمی‌شود. در Production اگر انبار پلتفرمی نباشد و این بخش ناقص یا نامعتبر باشد،
/// راه‌اندازی متوقف می‌شود؛ بیرون از Production فقط هشدار ثبت می‌شود.
/// </para>
/// <para>
/// متغیرهای محیطی: <c>PlatformWarehouse__Name</c>، <c>PlatformWarehouse__ProvinceName</c>،
/// <c>PlatformWarehouse__CityName</c>، <c>PlatformWarehouse__StreetAddress</c>، <c>PlatformWarehouse__Plaque</c>،
/// <c>PlatformWarehouse__Unit</c>، <c>PlatformWarehouse__PostalCode</c>، <c>PlatformWarehouse__RecipientFirstName</c>،
/// <c>PlatformWarehouse__RecipientLastName</c>، <c>PlatformWarehouse__PhoneNumber</c>.
/// </para>
/// </summary>
public sealed class PlatformWarehouseOptions
{
    public const string SectionName = "PlatformWarehouse";

    /// <summary>واحد پیش‌فرض نشانی وقتی <see cref="Unit"/> خالی است (ستون Unit در Address الزامی است).</summary>
    public const string DefaultUnit = "-";

    /// <summary>نام انبار (حداکثر ۱۵۰ نویسه).</summary>
    public string Name { get; set; } = "انبار مرکزی پلتفرم";

    /// <summary>نام استان؛ اگر استانی با همین نام باشد همان استفاده می‌شود، وگرنه ساخته می‌شود.</summary>
    public string ProvinceName { get; set; } = string.Empty;

    /// <summary>نام شهر در همان استان؛ اگر نباشد ساخته می‌شود.</summary>
    public string CityName { get; set; } = string.Empty;

    public string StreetAddress { get; set; } = string.Empty;

    public string Plaque { get; set; } = string.Empty;

    /// <summary>اختیاری؛ خالی یعنی <see cref="DefaultUnit"/>.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>کد پستی ده‌رقمی.</summary>
    public string PostalCode { get; set; } = string.Empty;

    public string RecipientFirstName { get; set; } = string.Empty;

    public string RecipientLastName { get; set; } = string.Empty;

    /// <summary>
    /// موبایل مسئول انبار؛ با <see cref="IranianPhoneNumber"/> نرمال می‌شود. مالک نشانی انبار کاربری با همین شماره است
    /// (اگر نباشد، یک حساب سیستمی غیرفعال با این شماره ساخته می‌شود) و شماره در توضیح نشانی هم ثبت می‌شود.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// آیا اپراتور این بخش را (دست‌کم یکی از فیلدهای نشانی) تنظیم کرده است؟ <see cref="Name"/> و <see cref="Unit"/>
    /// حساب نمی‌شوند چون پیش‌فرض یا اختیاری‌اند.
    /// </summary>
    public bool IsSpecified =>
        new[]
        {
            ProvinceName, CityName, StreetAddress, Plaque, PostalCode,
            RecipientFirstName, RecipientLastName, PhoneNumber
        }.Any(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>شمارهٔ نرمال‌شده یا null اگر نامعتبر باشد.</summary>
    public string? NormalizedPhoneNumber => IranianPhoneNumber.NormalizeOrNull(PhoneNumber);

    /// <summary>خطاهای پیکربندی را برمی‌گرداند؛ فهرست خالی یعنی می‌توان انبار را ساخت.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        Required(errors, nameof(Name), Name, 150);
        Required(errors, nameof(ProvinceName), ProvinceName, 100);
        Required(errors, nameof(CityName), CityName, 100);
        Required(errors, nameof(StreetAddress), StreetAddress, 300);
        Required(errors, nameof(Plaque), Plaque, 10);
        Required(errors, nameof(RecipientFirstName), RecipientFirstName, 50);
        Required(errors, nameof(RecipientLastName), RecipientLastName, 50);

        if (!string.IsNullOrWhiteSpace(Unit) && Unit.Trim().Length > 20)
            errors.Add($"{SectionName}:{nameof(Unit)} حداکثر 20 نویسه است.");

        var postalCode = PostalCode?.Trim() ?? string.Empty;
        if (postalCode.Length != 10 || !postalCode.All(char.IsAsciiDigit))
            errors.Add($"{SectionName}:{nameof(PostalCode)} باید کد پستی ده‌رقمی (فقط ارقام لاتین) باشد.");

        if (NormalizedPhoneNumber is null)
            errors.Add($"{SectionName}:{nameof(PhoneNumber)} شمارهٔ موبایل معتبر ایران نیست (مثال: 09123456789).");

        return errors;
    }

    private static void Required(List<string> errors, string key, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{SectionName}:{key} الزامی است.");
        else if (value.Trim().Length > maxLength)
            errors.Add($"{SectionName}:{key} حداکثر {maxLength} نویسه است.");
    }
}
