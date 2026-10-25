namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Phase 9 (R33) — بازمحاسبهٔ کش امتیاز.
///
/// قاعدهٔ طلایی: هر عملیاتی که مجموع نظرات تأییدشده را تغییر می‌دهد
/// (تأیید، رد، حذف) باید این سرویس را صدا بزند. تنها نقطهٔ نوشتن کش اینجاست
/// تا هیچ مسیری نتواند عدد را کهنه بگذارد.
/// </summary>
public interface IReviewRatingCacheService
{
    Task RecalculateProductAsync(int productId, CancellationToken cancellationToken = default);

    Task RecalculateSellerAsync(int sellerId, CancellationToken cancellationToken = default);
}