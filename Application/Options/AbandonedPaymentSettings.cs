namespace Application.Options;

/// <summary>Phase 9 (R44) — تنظیمات پاک‌سازی پرداخت‌های رهاشده.</summary>
public class AbandonedPaymentSettings
{
    public const string SectionName = "AbandonedPaymentSettings";

    /// <summary>هر چند دقیقه یک‌بار اجرا شود.</summary>
    public int ScanIntervalMinutes { get; set; } = 15;

    /// <summary>پرداخت Pending پس از این مدت رهاشده تلقی می‌شود.</summary>
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>برای خاموش کردن job در محیط تست/توسعه.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Batch 43 (FIN-05): پیش از لغو پرداخت (یا سفارش) رهاشده از درگاه پرسیده شود. با true (پیش‌فرض) پرداخت بدون پاسخ
    /// قطعی درگاه هرگز بی‌صدا لغو نمی‌شود: پرداخت‌شده ⇒ برداشت از مسیر Verify؛ پرداخت‌نشده ⇒ لغو؛ نامعلوم یا استعلامِ
    /// خاموش (<c>PaymentGatewaySettings:EnableInquiry = false</c>، پیش‌فرض تا پاسخ تصمیم باز N2) ⇒ Pending می‌ماند و
    /// یک بار به صف بررسی می‌رود. با false رفتار پیش از Batch 43 برمی‌گردد: لغو پس از مهلت، بدون پرسیدن از درگاه
    /// (پول تأییدنشده را درگاه برمی‌گرداند، ولی خریدارِ پرداخت‌کرده سفارشش را از دست می‌دهد).
    /// </summary>
    public bool InquireGatewayBeforeCancel { get; set; } = true;

    /// <summary>
    /// Batch 43: سقف پرداخت‌هایی که در هر اجرا بررسی می‌شوند (هر کدام یک تماس با درگاه). باقی به اجرای بعد می‌مانند.
    /// مقدار خارج از ۱ تا ۵۰۰۰ به همین بازه بریده می‌شود.
    /// </summary>
    public int MaxPaymentsPerSweep { get; set; } = 200;
}
