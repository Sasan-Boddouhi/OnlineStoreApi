namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Phase 9 (R40) — ثبت رویدادهای حساس در AuditLog.
///
/// قرارداد مهم: این سرویس <b>SaveChanges صدا نمی‌زند</b>.
/// رکورد حسابرسی باید در همان تراکنشِ عملیات تجاری ذخیره شود؛
/// اگر عملیات rollback شد، حسابرسی هم باید برگردد.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}