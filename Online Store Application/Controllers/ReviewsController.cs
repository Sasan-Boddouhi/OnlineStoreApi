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

    /// <summary>دریافت نظرات در انتظار تأیید محصولات (مخصوص ادمین).</summary>
    [HttpGet("product/pending")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IEnumerable<ProductReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ProductReviewDto>>> GetPendingProductReviews(CancellationToken cancellationToken)
        => Ok(await _reviewService.GetPendingProductReviewsAsync(cancellationToken));

    /// <summary>دریافت نظرات تأییدشدهٔ یک محصول.</summary>
    [HttpGet("product/{productId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<ProductReviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductReviewDto>>> GetProductReviews(
        int productId,
        CancellationToken cancellationToken)
        => Ok(await _reviewService.GetProductReviewsAsync(productId, cancellationToken));

    /// <summary>
    /// ثبت نظر محصول. فقط برای خریدارانی که سفارششان تحویل شده است.
    /// </summary>
    [HttpPost("product/{productId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ProductReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProductReviewDto>> AddProductReview(
        int productId,
        [FromBody] CreateProductReviewDto dto,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.TryGetCurrentUserId()
            ?? throw new BusinessException("کاربر شناسایی نشد.", "UNAUTHENTICATED");

        var review = await _reviewService.AddProductReviewAsync(userId, productId, dto, cancellationToken);
        return CreatedAtAction(nameof(GetProductReviews), new { productId }, review);
    }

    /// <summary>تأیید نظر محصول توسط ادمین.</summary>
    [HttpPost("product/{reviewId:int}/approve")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveProductReview(int reviewId, CancellationToken cancellationToken)
    {
        await _reviewService.ApproveProductReviewAsync(reviewId, cancellationToken);
        return NoContent();
    }

    /// <summary>رد نظر محصول توسط ادمین.</summary>
    [HttpPost("product/{id:int}/reject")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectProductReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.RejectProductReviewAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>حذف نظر محصول.</summary>
    [HttpDelete("product/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProductReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.DeleteProductReviewAsync(id, cancellationToken);
        return NoContent();
    }

    // ==================== فروشنده ====================

    /// <summary>دریافت نظرات در انتظار تأیید فروشندگان (مخصوص ادمین).</summary>
    [HttpGet("seller/pending")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IEnumerable<SellerReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SellerReviewDto>>> GetPendingSellerReviews(CancellationToken cancellationToken)
        => Ok(await _reviewService.GetPendingSellerReviewsAsync(cancellationToken));

    /// <summary>دریافت نظرات تأییدشدهٔ یک فروشنده.</summary>
    [HttpGet("seller/{sellerId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<SellerReviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<SellerReviewDto>>> GetSellerReviews(
        int sellerId,
        CancellationToken cancellationToken)
        => Ok(await _reviewService.GetSellerReviewsAsync(sellerId, cancellationToken));

    /// <summary>ثبت نظر برای فروشنده.</summary>
    [HttpPost("seller/{sellerId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(SellerReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SellerReviewDto>> AddSellerReview(
        int sellerId,
        [FromBody] CreateSellerReviewDto dto,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.TryGetCurrentUserId()
            ?? throw new BusinessException("کاربر شناسایی نشد.", "UNAUTHENTICATED");

        var review = await _reviewService.AddSellerReviewAsync(userId, sellerId, dto, cancellationToken);
        return CreatedAtAction(nameof(GetSellerReviews), new { sellerId }, review);
    }

    /// <summary>تأیید نظر فروشنده توسط ادمین.</summary>
    [HttpPost("seller/{reviewId:int}/approve")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveSellerReview(int reviewId, CancellationToken cancellationToken)
    {
        await _reviewService.ApproveSellerReviewAsync(reviewId, cancellationToken);
        return NoContent();
    }

    /// <summary>رد نظر فروشنده توسط ادمین.</summary>
    [HttpPost("seller/{id:int}/reject")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectSellerReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.RejectSellerReviewAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>حذف نظر فروشنده.</summary>
    [HttpDelete("seller/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSellerReview(int id, CancellationToken cancellationToken)
    {
        await _reviewService.DeleteSellerReviewAsync(id, cancellationToken);
        return NoContent();
    }
}