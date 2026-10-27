using Asp.Versioning;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Review;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>Phase 5 — نظرات محصول و فروشنده.</summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly ICurrentUserService _currentUser;

    public ReviewsController(IReviewService reviewService, ICurrentUserService currentUser)
    {
        _reviewService = reviewService;
        _currentUser = currentUser;
    }

    // ==================== محصول ====================

    [HttpGet("product/pending")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<ProductReviewDto>>> GetPendingProductReviews(CancellationToken cancellationToken)
        => Ok(await _reviewService.GetPendingProductReviewsAsync(cancellationToken));

    [HttpGet("product/{productId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ProductReviewDto>>> GetProductReviews(
        int productId,
        CancellationToken cancellationToken)
        => Ok(await _reviewService.GetProductReviewsAsync(productId, cancellationToken));

    /// <summary>
    /// ثبت نظر محصول. فقط برای خریدارانی که سفارششان تحویل شده است.
    /// </summary>
    [HttpPost("product/{productId:int}")]
    [Authorize]
    public async Task<ActionResult<ProductReviewDto>> AddProductReview(
        int productId,
        [FromBody] CreateProductReviewDto dto,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.TryGetCurrentUserId()
            ?? throw new BusinessException("کاربر شناسایی نشد.", "UNAUTHENTICATED");

        var review = await _reviewService.AddProductReviewAsync(userId, productId, dto, cancellationToken);
        return Ok(review);
    }

    [HttpPost("product/{reviewId:int}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ApproveProductReview(int reviewId, CancellationToken cancellationToken)
    {
        await _reviewService.ApproveProductReviewAsync(reviewId, cancellationToken);
        return NoContent();
    }

    [HttpPost("product/{id:int}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectProductReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.RejectProductReviewAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("product/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteProductReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.DeleteProductReviewAsync(id, cancellationToken);
        return NoContent();
    }

    // ==================== فروشنده ====================

    [HttpGet("seller/pending")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<SellerReviewDto>>> GetPendingSellerReviews(CancellationToken cancellationToken)
        => Ok(await _reviewService.GetPendingSellerReviewsAsync(cancellationToken));

    [HttpGet("seller/{sellerId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SellerReviewDto>>> GetSellerReviews(
        int sellerId,
        CancellationToken cancellationToken)
        => Ok(await _reviewService.GetSellerReviewsAsync(sellerId, cancellationToken));

    [HttpPost("seller/{sellerId:int}")]
    [Authorize]
    public async Task<ActionResult<SellerReviewDto>> AddSellerReview(
        int sellerId,
        [FromBody] CreateSellerReviewDto dto,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.TryGetCurrentUserId()
            ?? throw new BusinessException("کاربر شناسایی نشد.", "UNAUTHENTICATED");

        var review = await _reviewService.AddSellerReviewAsync(userId, sellerId, dto, cancellationToken);
        return Ok(review);
    }

    [HttpPost("seller/{reviewId:int}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ApproveSellerReview(int reviewId, CancellationToken cancellationToken)
    {
        await _reviewService.ApproveSellerReviewAsync(reviewId, cancellationToken);
        return NoContent();
    }

    [HttpPost("seller/{id:int}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectSellerReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.RejectSellerReviewAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("seller/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteSellerReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.DeleteSellerReviewAsync(id, cancellationToken);
        return NoContent();
    }
}