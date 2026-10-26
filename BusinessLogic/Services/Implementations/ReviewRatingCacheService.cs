using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 9 (R33) — محاسبهٔ مجدد کش امتیاز.
///
/// فقط نظرات <c>IsApproved == true</c> شمرده می‌شوند. نظرات تأییدشده با <see cref="IGenericRepository{T}.ListAsync"/>
/// بارگذاری شده و میانگین در حافظه با <c>reviews.Average(...)</c> محاسبه می‌شود.
/// </summary>
public sealed class ReviewRatingCacheService : IReviewRatingCacheService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReviewRatingCacheService> _logger;

    public ReviewRatingCacheService(
        IUnitOfWork unitOfWork,
        ILogger<ReviewRatingCacheService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task RecalculateProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        var reviews = await _unitOfWork.Repository<ProductReview>()
            .ListAsync(
                new Spec<ProductReview>().Where(r => r.ProductId == productId && r.IsApproved),
                cancellationToken);

        var count = reviews.Count;
        var average = count == 0 ? 0d : Math.Round(reviews.Average(r => (double)r.Rating), 2);

        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);
        if (product is null)
            return;

        product.AverageRating = average;
        product.ReviewCount = count;

        _unitOfWork.Repository<Product>().Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rating cache updated for product {ProductId}: {Average} / {Count}",
            productId, average, count);
    }

    public async Task RecalculateSellerAsync(int sellerId, CancellationToken cancellationToken = default)
    {
        var reviews = await _unitOfWork.Repository<SellerReview>()
            .ListAsync(
                new Spec<SellerReview>().Where(r => r.SellerId == sellerId && r.IsApproved),
                cancellationToken);

        var count = reviews.Count;
        var average = count == 0 ? 0d : Math.Round(reviews.Average(r => (double)r.Rating), 2);

        var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
        if (seller is null)
            return;

        seller.AverageRating = average;
        seller.ReviewCount = count;

        _unitOfWork.Repository<Seller>().Update(seller);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rating cache updated for seller {SellerId}: {Average} / {Count}",
            sellerId, average, count);
    }
}