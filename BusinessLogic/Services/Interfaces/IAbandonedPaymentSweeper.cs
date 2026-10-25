namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Phase 9 (R44) — پاک‌سازی پرداخت‌های رهاشده.
/// منطق از BackgroundService جدا شده تا بتوان بدون اجرای هاست تستش کرد.
/// </summary>
public interface IAbandonedPaymentSweeper
{
    /// <summary>
    /// پرداخت‌های Pending قدیمی‌تر از مهلت را Cancelled می‌کند
    /// و تعداد ردیف‌های تغییرکرده را برمی‌گرداند.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}