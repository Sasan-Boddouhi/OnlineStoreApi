namespace Application.Options;

/// <summary>
/// F-03: سقف‌های محدودیت نرخ برای هر سیاست. هر سیاست برای هر کلاینت یک سهمیهٔ جداگانه دارد:
/// ورود، ثبت‌نام و تازه‌سازی توکن بر اساس IP کلاینت؛ checkout و شروع پرداخت بر اساس شناسهٔ کاربر
/// (و اگر کاربر احراز نشده باشد، IP). مقدارهای پیش‌فرض همان سقف‌های قبلی هستند.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowLimitOptions Login { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public FixedWindowLimitOptions Register { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public FixedWindowLimitOptions Refresh { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    public FixedWindowLimitOptions Checkout { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public FixedWindowLimitOptions PaymentInitiate { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}

/// <summary>یک پنجرهٔ ثابت: حداکثر <see cref="PermitLimit"/> درخواست در هر <see cref="WindowSeconds"/> ثانیه.</summary>
public sealed class FixedWindowLimitOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
