namespace BusinessLogic.DTOs.Product;

/// <summary>
/// پارامترهای query فهرست محصولات (<c>GET /api/v1/products</c>). Batch 60 (API-10).
/// <para>
/// همهٔ شرط‌ها با AND ترکیب می‌شوند: پارامترهای نوع‌دار (<see cref="Q"/>، <see cref="CategoryId"/>،
/// <see cref="SubcategoryId"/>، <see cref="BrandId"/>، <see cref="InStock"/>) و فیلتر زبان کوچک
/// (<see cref="Filter"/>) و قاعدهٔ «فقط محصولات فعال».
/// </para>
/// </summary>
public sealed class ProductListQueryDto
{
    /// <summary>جستجوی متنی در نام یا توضیح محصول (شامل‌بودن، حداکثر ۱۰۰ نویسه).</summary>
    public string? Q { get; set; }

    /// <summary>فقط محصولات زیردسته‌های این دسته.</summary>
    public int? CategoryId { get; set; }

    /// <summary>فقط محصولات این زیردسته.</summary>
    public int? SubcategoryId { get; set; }

    /// <summary>فقط محصولات این برند.</summary>
    public int? BrandId { get; set; }

    /// <summary>
    /// <c>true</c>: فقط محصولاتی که دست‌کم یک پیشنهاد فروش قابل خرید دارند (واریانت فعال، پیشنهاد فعال،
    /// فروشندهٔ تأییدشده و فعال، موجودی فعال بیشتر از صفر). <c>false</c>: فقط محصولاتی که هیچ‌کدام ندارند.
    /// </summary>
    public bool? InStock { get; set; }

    /// <summary>فیلتر زبان کوچک؛ دستور زبان در <c>docs/api-query-syntax.md</c>.</summary>
    public string? Filter { get; set; }

    /// <summary>
    /// یکی از <c>newest</c>، <c>rating</c>، <c>name</c>؛ یا برای سازگاری، فهرست فیلدهای مجاز با ویرگول
    /// و <c>-</c> برای نزولی (مثل <c>-name</c>). مقدار ناشناخته ← 422.
    /// </summary>
    public string? Sort { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    /// <summary>فقط برای دارندهٔ مجوز مدیریت کاتالوگ اثر دارد؛ برای بقیه بی‌صدا نادیده گرفته می‌شود.</summary>
    public bool IncludeInactive { get; set; }
}

/// <summary>مرتب‌سازی‌های آمادهٔ فهرست محصولات.</summary>
public enum ProductListSort
{
    /// <summary>جدیدترین اول (زمان ایجاد نزولی).</summary>
    Newest,

    /// <summary>بیشترین امتیاز اول (میانگین امتیاز، سپس تعداد نظر).</summary>
    Rating,

    /// <summary>نام صعودی.</summary>
    Name
}

/// <summary>شرط‌های نوع‌دار فهرست محصولات که سرویس روی query اعمال می‌کند.</summary>
public sealed record ProductListCriteria
{
    public static readonly ProductListCriteria None = new();

    public string? Q { get; init; }
    public int? CategoryId { get; init; }
    public int? SubcategoryId { get; init; }
    public int? BrandId { get; init; }
    public bool? InStock { get; init; }

    /// <summary>اگر مقدار داشته باشد جای مرتب‌سازی زبان کوچک را می‌گیرد.</summary>
    public ProductListSort? Sort { get; init; }

    /// <summary>مقدار آمادهٔ <c>sort</c> را می‌شناسد (بدون حساسیت به حروف)؛ برای هر مقدار دیگر <c>null</c>.</summary>
    public static ProductListSort? ParseSortPreset(string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "newest" => ProductListSort.Newest,
            "rating" => ProductListSort.Rating,
            "name" => ProductListSort.Name,
            _ => null
        };
    }
}
