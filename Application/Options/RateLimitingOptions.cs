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

    // Batch 11b: 20 → 60. توکن دسترسی ۱۵ دقیقه‌ای است (تازه‌سازی بیشتر) و کاربران موبایل زیادی پشت
    // یک IP مشترک اپراتور (CGNAT) هستند؛ این سقف برای هر IP است.
    public FixedWindowLimitOptions Refresh { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public FixedWindowLimitOptions Checkout { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public FixedWindowLimitOptions PaymentInitiate { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    // Batch 43 (FIN-05): هر تأیید پرداخت یک تماس با درگاه است؛ سقف برای هر کاربر (و اگر احراز نشده، IP).
    // یک بازگشت عادی از درگاه یک یا دو تأیید می‌فرستد. کلاینتی که 503 PAYMENT_VERIFY_RETRY را هر ۵ ثانیه تکرار کند پس از
    // حدود ۵۰ ثانیه 429 با Retry-After می‌گیرد؛ پرداختِ بی‌نتیجه را جاب تطبیق (AbandonedPaymentSweeper) قطعی می‌کند.
    public FixedWindowLimitOptions PaymentVerify { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    // Batch 46 (review §9.3 قاعدهٔ ۲ و ۳): سقف هر IP برای ارسال کد (هر پیامک هزینه دارد) و برای تأیید کد.
    // سقف هر شماره و بودجهٔ روزانهٔ کل در OtpService (Otp:*) است.
    public FixedWindowLimitOptions OtpSend { get; set; } = new() { PermitLimit = 10, WindowSeconds = 3600 };

    public FixedWindowLimitOptions OtpVerify { get; set; } = new() { PermitLimit = 30, WindowSeconds = 600 };
}

/// <summary>یک پنجرهٔ ثابت: حداکثر <see cref="PermitLimit"/> درخواست در هر <see cref="WindowSeconds"/> ثانیه.</summary>
public sealed class FixedWindowLimitOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
