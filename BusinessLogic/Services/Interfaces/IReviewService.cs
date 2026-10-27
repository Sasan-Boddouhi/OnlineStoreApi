using BusinessLogic.DTOs.Review;

namespace BusinessLogic.Services.Interfaces
{
    /// <summary>
    /// Phase 5 — نظرات.
    /// قانون اصلی: هیچ نظری بدون خرید تحویل‌شده ثبت نمی‌شود.
    /// </summary>
    public interface IReviewService
    {
        Task<ProductReviewDto> AddProductReviewAsync(
            int userId,
            int productId,
            CreateProductReviewDto dto,
            CancellationToken cancellationToken = default);

        /// <summary>فقط نظرات تأییدشده.</summary>
        Task<IEnumerable<ProductReviewDto>> GetProductReviewsAsync(int productId, CancellationToken cancellationToken = default);

        Task<SellerReviewDto> AddSellerReviewAsync(
            int userId,
            int sellerId,
            CreateSellerReviewDto dto,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<SellerReviewDto>> GetSellerReviewsAsync(int sellerId, CancellationToken cancellationToken = default);

        Task ApproveProductReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task RejectProductReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task DeleteProductReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task ApproveSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task RejectSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task DeleteSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default);

        Task<IEnumerable<ProductReviewDto>> GetPendingProductReviewsAsync(CancellationToken cancellationToken = default);

        Task<IEnumerable<SellerReviewDto>> GetPendingSellerReviewsAsync(CancellationToken cancellationToken = default);
    }
}