using BusinessLogic.DTOs.Media;

namespace BusinessLogic.Services.Interfaces
{
    /// <summary>Phase 5 — مدیریت گالری تصاویر محصول.</summary>
    public interface IProductImageService
    {
        /// <summary>آپلود فایل و ثبت آن در گالری.</summary>
        Task<ProductImageDto> UploadAsync(
            int productId,
            Stream content,
            string fileName,
            string contentType,
            string? alt,
            bool isPrimary,
            CancellationToken cancellationToken = default);

        /// <summary>ثبت تصویر با URL آماده (وقتی فایل جای دیگری میزبانی می‌شود).</summary>
        Task<ProductImageDto> AddAsync(
            int productId,
            string url,
            string? alt,
            bool isPrimary,
            CancellationToken cancellationToken = default);

        /// <summary>حذف تصویر از گالری و از دیسک.</summary>
        Task DeleteAsync(int productId, int imageId, CancellationToken cancellationToken = default);

        Task<IEnumerable<ProductImageDto>> GetForProductAsync(int productId, CancellationToken cancellationToken = default);
    }
}