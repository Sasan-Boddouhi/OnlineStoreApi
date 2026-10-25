namespace Application.Options;

/// <summary>
/// تنظیمات موجودی (Phase 0/1 — تصمیم ناظر: گزینهٔ C).
///
/// EnforceStock = true  : محصول بدون ردیف موجودی قابل فروش نیست (رفتار سخت‌گیرانه، پیش‌فرض).
/// EnforceStock = false : اگر محصول هیچ ردیف موجودی فعالی نداشته باشد، «نامحدود» تلقی می‌شود
///                        (با LogWarning). این حالت برای بازهٔ مهاجرت است تا با استقرار نسخهٔ جدید،
///                        فروش محصولات قدیمی که هنوز موجودی اولیه‌شان ثبت نشده متوقف نشود.
///
/// توجه: این پرچم فقط حالت «هیچ ردیفی وجود ندارد» را نرم می‌کند.
/// اگر ردیف موجودی وجود داشته باشد و کافی نباشد، حتی با false هم خطای INSUFFICIENT_STOCK رخ می‌دهد.
/// </summary>
public sealed class InventorySettings
{
    public const string SectionName = "InventorySettings";

    public bool EnforceStock { get; set; } = true;
}