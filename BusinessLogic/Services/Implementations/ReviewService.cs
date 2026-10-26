using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Review;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    /// <summary>
    /// Phase 5 — نظرات محصول و فروشنده.
    ///
    /// قانون سخت: نظر فقط با خرید تحویل‌شده ثبت می‌شود. بدون این قید،
    /// امتیاز محصول با نظرات بی‌اعتبار پر می‌شود و کل سیستم اعتماد از بین می‌رود.
    /// </summary>
    public sealed class ReviewService : IReviewService
    {
        private const int MaxCommentLength = 1000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IReviewRatingCacheService _ratingCache;
        private readonly ILogger<ReviewService> _logger;

        public ReviewService(
            IUnitOfWork unitOfWork,
            IReviewRatingCacheService ratingCache,
            ILogger<ReviewService> logger)
        {
            _unitOfWork = unitOfWork;
            _ratingCache = ratingCache;
            _logger = logger;
        }

        // ==================== محصول ====================

        public async Task<ProductReviewDto> AddProductReviewAsync(
            int userId,
            int productId,
            CreateProductReviewDto dto,
            CancellationToken cancellationToken = default)
        {
            ValidateRating(dto.Rating);
            ValidateComment(dto.Comment);

            var customer = await GetCustomerAsync(userId, cancellationToken);
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);

            if (product is null)
                throw new NotFoundException("محصول یافت نشد.", "PRODUCT_NOT_FOUND");

            // اثبات خرید: آیتم سفارش تحویل‌شده‌ای که همین محصول را دارد.
            var purchasedItem = await _unitOfWork.Repository<OrderItem>()
                .FirstOrDefaultAsync(
                    new Spec<OrderItem>()
                        .Where(oi => oi.Order.Customer.UserId == userId
                                  && oi.Order.Status == OrderStatus.Delivered
                                  && oi.Listing.Variant.ProductId == productId)
                        .OrderByDescending(oi => oi.OrderItemId),
                    cancellationToken);

            if (purchasedItem is null)
                throw new BusinessException(
                    "ثبت نظر فقط برای محصولاتی ممکن است که سفارش آن‌ها تحویل شده باشد.",
                    "REVIEW_REQUIRES_DELIVERED_PURCHASE");

            var alreadyReviewed = await _unitOfWork.Repository<ProductReview>().AnyAsync(
                new Spec<ProductReview>()
                    .Where(r => r.ProductId == productId && r.CustomerId == customer.CustomerId),
                cancellationToken);

            if (alreadyReviewed)
                throw new BusinessException("برای این محصول قبلاً نظر ثبت کرده‌اید.", "REVIEW_ALREADY_EXISTS");

            var review = new ProductReview
            {
                ProductId = productId,
                CustomerId = customer.CustomerId,
                OrderItemId = purchasedItem.OrderItemId,
                Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim(),
                IsApproved = false
            };

            review.SetRating(dto.Rating);

            await _unitOfWork.Repository<ProductReview>().AddAsync(review, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Product review {ReviewId} created for product {ProductId} by customer {CustomerId}",
                review.ProductReviewId, productId, customer.CustomerId);

            return new ProductReviewDto
            {
                ProductReviewId = review.ProductReviewId,
                ProductId = review.ProductId,
                CustomerId = review.CustomerId,
                CustomerName = BuildName(customer.User),
                Rating = review.Rating,
                Comment = review.Comment,
                IsApproved = review.IsApproved,
                CreatedOn = review.CreatedOn
            };
        }

        public async Task<IEnumerable<ProductReviewDto>> GetProductReviewsAsync(
            int productId,
            CancellationToken cancellationToken = default)
        {
            var reviews = await _unitOfWork.Repository<ProductReview>()
                .ListAsync(
                    new Spec<ProductReview>()
                        .Where(r => r.ProductId == productId && r.IsApproved)
                        .OrderByDescending(r => r.CreatedOn),
                    cancellationToken);

            var names = await LoadCustomerNamesAsync(reviews.Select(r => r.CustomerId), cancellationToken);

            return reviews.Select(r => new ProductReviewDto
            {
                ProductReviewId = r.ProductReviewId,
                ProductId = r.ProductId,
                CustomerId = r.CustomerId,
                CustomerName = names.GetValueOrDefault(r.CustomerId, string.Empty),
                Rating = r.Rating,
                Comment = r.Comment,
                IsApproved = r.IsApproved,
                CreatedOn = r.CreatedOn
            });
        }

        // ==================== فروشنده ====================

        public async Task<SellerReviewDto> AddSellerReviewAsync(
            int userId,
            int sellerId,
            CreateSellerReviewDto dto,
            CancellationToken cancellationToken = default)
        {
            ValidateRating(dto.Rating);
            ValidateComment(dto.Comment);

            var customer = await GetCustomerAsync(userId, cancellationToken);

            var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
            if (seller is null)
                throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

            var purchased = await _unitOfWork.Repository<OrderItem>()
                .AnyAsync(
                    new Spec<OrderItem>()
                        .Where(oi => oi.Order.Customer.UserId == userId
                                  && oi.Order.Status == OrderStatus.Delivered
                                  && oi.Listing.SellerId == sellerId),
                    cancellationToken);

            if (!purchased)
                throw new BusinessException(
                    "ثبت نظر برای فروشنده فقط پس از تحویل سفارش او ممکن است.",
                    "REVIEW_REQUIRES_DELIVERED_PURCHASE");

            var alreadyReviewed = await _unitOfWork.Repository<SellerReview>().AnyAsync(
                new Spec<SellerReview>()
                    .Where(r => r.SellerId == sellerId && r.CustomerId == customer.CustomerId),
                cancellationToken);

            if (alreadyReviewed)
                throw new BusinessException("برای این فروشنده قبلاً نظر ثبت کرده‌اید.", "REVIEW_ALREADY_EXISTS");

            var review = new SellerReview
            {
                SellerId = sellerId,
                CustomerId = customer.CustomerId,
                Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim(),
                IsApproved = false
            };

            review.SetRating(dto.Rating);

            await _unitOfWork.Repository<SellerReview>().AddAsync(review, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Seller review {ReviewId} created for seller {SellerId} by customer {CustomerId}",
                review.SellerReviewId, sellerId, customer.CustomerId);

            return new SellerReviewDto
            {
                SellerReviewId = review.SellerReviewId,
                SellerId = review.SellerId,
                CustomerId = review.CustomerId,
                CustomerName = BuildName(customer.User),
                Rating = review.Rating,
                Comment = review.Comment,
                IsApproved = review.IsApproved,
                CreatedOn = review.CreatedOn
            };
        }

        public async Task<IEnumerable<SellerReviewDto>> GetSellerReviewsAsync(
            int sellerId,
            CancellationToken cancellationToken = default)
        {
            var reviews = await _unitOfWork.Repository<SellerReview>()
                .ListAsync(
                    new Spec<SellerReview>()
                        .Where(r => r.SellerId == sellerId && r.IsApproved)
                        .OrderByDescending(r => r.CreatedOn),
                    cancellationToken);

            var names = await LoadCustomerNamesAsync(reviews.Select(r => r.CustomerId), cancellationToken);

            return reviews.Select(r => new SellerReviewDto
            {
                SellerReviewId = r.SellerReviewId,
                SellerId = r.SellerId,
                CustomerId = r.CustomerId,
                CustomerName = names.GetValueOrDefault(r.CustomerId, string.Empty),
                Rating = r.Rating,
                Comment = r.Comment,
                IsApproved = r.IsApproved,
                CreatedOn = r.CreatedOn
            });
        }

        // ==================== بازبینی ====================

        public async Task ApproveProductReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<ProductReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            if (review.IsApproved)
                return;

            review.IsApproved = true;
            _unitOfWork.Repository<ProductReview>().Update(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Phase 9 (R33): کش امتیاز باید فوراً تازه شود، وگرنه صفحهٔ محصول
            // عدد قدیمی را نشان می‌دهد و هیچ خطایی هم رخ نمی‌دهد.
            await _ratingCache.RecalculateProductAsync(review.ProductId, cancellationToken);

            _logger.LogInformation("Product review {ReviewId} approved", reviewId);
        }

        public async Task RejectProductReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<ProductReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            if (!review.IsApproved)
                return;

            review.IsApproved = false;
            _unitOfWork.Repository<ProductReview>().Update(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _ratingCache.RecalculateProductAsync(review.ProductId, cancellationToken);

            _logger.LogInformation("Product review {ReviewId} rejected", reviewId);
        }

        public async Task DeleteProductReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<ProductReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            var wasApproved = review.IsApproved;
            var productId = review.ProductId;

            _unitOfWork.Repository<ProductReview>().Delete(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasApproved)
            {
                await _ratingCache.RecalculateProductAsync(productId, cancellationToken);
            }

            _logger.LogInformation("Product review {ReviewId} deleted", reviewId);
        }

        public async Task ApproveSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<SellerReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            if (review.IsApproved)
                return;

            review.IsApproved = true;
            _unitOfWork.Repository<SellerReview>().Update(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _ratingCache.RecalculateSellerAsync(review.SellerId, cancellationToken);

            _logger.LogInformation("Seller review {ReviewId} approved", reviewId);
        }

        public async Task RejectSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<SellerReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            if (!review.IsApproved)
                return;

            review.IsApproved = false;
            _unitOfWork.Repository<SellerReview>().Update(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _ratingCache.RecalculateSellerAsync(review.SellerId, cancellationToken);

            _logger.LogInformation("Seller review {ReviewId} rejected", reviewId);
        }

        public async Task DeleteSellerReviewAsync(int reviewId, CancellationToken cancellationToken = default)
        {
            var review = await _unitOfWork.Repository<SellerReview>().GetByIdAsync(reviewId, cancellationToken);
            if (review is null)
                throw new NotFoundException("نظر یافت نشد.", "REVIEW_NOT_FOUND");

            var wasApproved = review.IsApproved;
            var sellerId = review.SellerId;

            _unitOfWork.Repository<SellerReview>().Delete(review);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasApproved)
            {
                await _ratingCache.RecalculateSellerAsync(sellerId, cancellationToken);
            }

            _logger.LogInformation("Seller review {ReviewId} deleted", reviewId);
        }

        // ==================== ابزار داخلی ====================

        private async Task<Customer> GetCustomerAsync(int userId, CancellationToken cancellationToken)
        {
            var customer = await _unitOfWork.Repository<Customer>()
                .FirstOrDefaultAsync(
                    new Spec<Customer>()
                        .Where(c => c.UserId == userId)
                        .Include(c => c.User),
                    cancellationToken);

            if (customer is null)
                throw new BusinessException("پروفایل مشتری یافت نشد.", "CUSTOMER_NOT_FOUND");

            return customer;
        }

        /// <summary>
        /// نام مشتریان جداگانه بارگذاری می‌شود چون Spec فعلی Include تودرتو ندارد
        /// (Customer.User نیاز به دو سطح دارد).
        /// </summary>
        private async Task<Dictionary<int, string>> LoadCustomerNamesAsync(
            IEnumerable<int> customerIds,
            CancellationToken cancellationToken)
        {
            var ids = customerIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<int, string>();

            var customers = await _unitOfWork.Repository<Customer>()
                .ListAsync(
                    new Spec<Customer>()
                        .Where(c => ids.Contains(c.CustomerId))
                        .Include(c => c.User),
                    cancellationToken);

            return customers.ToDictionary(c => c.CustomerId, c => BuildName(c.User));
        }

        private static string BuildName(User? user) =>
            user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();

        private static void ValidateRating(int rating)
        {
            if (rating < ProductReview.MinRating || rating > ProductReview.MaxRating)
                throw new BusinessException(
                    $"امتیاز باید بین {ProductReview.MinRating} و {ProductReview.MaxRating} باشد.",
                    "INVALID_RATING");
        }

        private static void ValidateComment(string? comment)
        {
            if (comment is not null && comment.Length > MaxCommentLength)
                throw new BusinessException(
                    $"متن نظر حداکثر {MaxCommentLength} کاراکتر است.", "COMMENT_TOO_LONG");
        }
    }
}