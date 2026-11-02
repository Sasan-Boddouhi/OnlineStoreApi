using Application.Exceptions;
using Application.Interfaces;
using Asp.Versioning;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 2 — مدیریت فروشندگان. مجوزدهی با Policyهای موجود انجام می‌شود (RBAC فاز ۱)،
/// نه با بررسی دستی نقش.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class SellersController : ControllerBase
{
    private readonly ISellerService _sellerService;
    private readonly ICurrentUserService _currentUserService;

    public SellersController(ISellerService sellerService, ICurrentUserService currentUserService)
    {
        _sellerService = sellerService;
        _currentUserService = currentUserService;
    }

    /// <summary>ایجاد فروشگاه و ثبت درخواست فروشندگی.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(SellerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SellerDto>> Create(
        [FromBody] CreateSellerDto dto,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
        {
            var userId = _currentUserService.GetCurrentUserId();
            dto.UserId = userId;
        }

        var created = await _sellerService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.SellerId }, created);
    }

    /// <summary>دریافت فهرست تمام فروشندگان (مخصوص ادمین).</summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IEnumerable<SellerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SellerDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await _sellerService.GetAllAsync(cancellationToken));

    /// <summary>
    /// Phase 8 (R36): پروفایل فروشگاهِ کاربر لاگین‌شده.
    ///
    /// چرا لازم است: بدون این endpoint فروشنده باید sellerId خودش را از جای دیگری
    /// می‌دانست؛ و هر جای دیگری که اشتباه پر شود، یعنی دیدن داشبورد فروشندهٔ دیگر.
    /// 404 برای کاربری که فروشگاه ندارد عمدی است — نه 403، تا وجود/عدم وجود فروشگاه لو نرود.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(SellerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerDto>> GetMe(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetCurrentUserId();

        var seller = await _sellerService.GetByUserIdAsync(userId, cancellationToken);

        if (seller is null)
            throw new NotFoundException("برای این کاربر فروشگاهی ثبت نشده است.", "SELLER_NOT_FOUND");

        return Ok(seller);
    }

    /// <summary>پروفایل عمومی فروشگاه برای صفحهٔ فروشنده.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SellerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerDto>> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _sellerService.GetByIdAsync(id, cancellationToken));

    /// <summary>ویرایش مشخصات فروشگاه.</summary>
    [HttpPut("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(SellerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SellerDto>> Update(
        int id,
        [FromBody] UpdateSellerDto dto,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
        {
            var userId = _currentUserService.GetCurrentUserId();
            var seller = await _sellerService.GetByIdAsync(id, cancellationToken);
            if (seller == null) return NotFound();
            if (seller.UserId != userId)
                return Forbid();
        }

        return Ok(await _sellerService.UpdateAsync(id, dto, cancellationToken));
    }

    /// <summary>حذف فروشگاه توسط ادمین.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _sellerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}