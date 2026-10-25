namespace Application.Interfaces
{
    /// <summary>
    /// Phase 5 — ذخیره‌سازی فایل (تصاویر محصول).
    /// پیاده‌سازی فعلی روی دیسک محلی است؛ برای S3/Cloudinary فقط این قرارداد
    /// پیاده‌سازی می‌شود و هیچ کد بالادستی عوض نمی‌شود.
    /// </summary>
    public interface IStorageService
    {
        /// <summary>فایل را ذخیره می‌کند و URL عمومی قابل دسترس را برمی‌گرداند.</summary>
        Task<string> SaveAsync(
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default);

        /// <summary>فایل را حذف می‌کند. اگر فایل نباشد، خطا نمی‌دهد.</summary>
        Task DeleteAsync(string url, CancellationToken cancellationToken = default);
    }
}