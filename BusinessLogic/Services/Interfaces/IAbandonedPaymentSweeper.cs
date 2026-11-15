namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Phase 9 (R44) — پاک‌سازی پرداخت‌های رهاشده.
/// منطق از BackgroundService جدا شده تا بتوان بدون اجرای هاست تستش کرد.
/// </summary>
public interface IAbandonedPaymentSweeper
{
    /// <summary>
    /// پرداخت‌های Pending قدیمی‌تر از مهلت را با درگاه تطبیق می‌دهد (Batch 43): پرداخت‌شده ⇒ برداشت از مسیر Verify،
    /// پرداخت‌نشده ⇒ Cancelled، نامعلوم ⇒ Pending می‌ماند و یک بار برای بررسی علامت می‌خورد.
    /// تعداد پرداخت‌های <b>لغوشده</b> را برمی‌گرداند.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}
