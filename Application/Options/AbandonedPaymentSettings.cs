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
}