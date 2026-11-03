using System.Security.Claims;
using Application.Exceptions;
using Application.Interfaces;
using Asp.Versioning;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 2 — پیشنهادهای فروش (Listing). قیمت اینجا مرجع قیمت سفارش است (D2).
/// موجودی روی Listing نیست (D1)؛ از Inventory خوانده می‌شود.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ListingsController : ControllerBase
{
    private readonly ISellerService _sellerService;
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUserService;

    public ListingsController(
        ISellerService sellerService,
        IInventoryService inventoryService,
        ICurrentUserService currentUserService)
    {
        _sellerService = sellerService;
        _inventoryService = inventoryService;
        _currentUserService = currentUserService;
    }

    private async Task<int> GetCurrentSellerIdAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int userId;
        if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedId))
        {
            userId = parsedId;
        }
        else
        {
            userId = _currentUserService.GetCurrentUserId();
        }

        var seller = await _sellerService.GetByUserIdAsync(userId, cancellationToken);
        if (seller is null)
            throw new ForbiddenException("کاربر جاری فروشنده نیست.", "NOT_A_SELLER");

        return seller.SellerId;
    }

    /// <summary>دریافت فهرست پیشنهادهای فروش.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<SellerProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<SellerProductDto>>> GetAll(
        [FromQuery] int? sellerId,
        CancellationToken cancellationToken)
        => Ok(await _sellerService.GetListingsAsync(sellerId, cancellationToken));

    /// <summary>دریافت مشخصات یک پیشنهاد فروش بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SellerProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerProductDto>> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _sellerService.GetListingAsync(id, cancellationToken));

    /// <summary>دریافت فهرست پیشنهادهای فروش یک واریانت مشخص.</summary>
    [HttpGet("variant/{variantId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<VariantListingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<VariantListingDto>>> GetByVariant(
        int variantId,
        CancellationToken cancellationToken)
        => Ok(await _sellerService.GetListingsByVariantIdAsync(variantId, cancellationToken));

    /// <summary>ایجاد پیشنهاد فروش جدید توسط فروشنده.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(SellerProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SellerProductDto>> Create(
        [FromBody] CreateSellerProductDto dto,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
        {
            var sellerId = await GetCurrentSellerIdAsync(cancellationToken);
            dto.SellerId = sellerId;
        }

        var created = await _sellerService.CreateListingAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.ListingId }, created);
    }

    /// <summary>ویرایش پیشنهاد فروش.</summary>
    [HttpPut("{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateSellerProductDto dto,
        CancellationToken cancellationToken)
    {
        var sellerId = await GetCurrentSellerIdAsync(cancellationToken);
        await _sellerService.UpdateListingAsync(sellerId, id, dto, cancellationToken);
        return NoContent();
    }

    /// <summary>حذف پیشنهاد فروش.</summary>
    [HttpDelete("{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var sellerId = await GetCurrentSellerIdAsync(cancellationToken);
        var success = await _sellerService.DeleteListingAsync(sellerId, id, cancellationToken);
        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>تغییر قیمت پیشنهاد فروش.</summary>
    [HttpPatch("{id:int}/price")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePrice(
        int id,
        [FromBody] UpdateListingPriceRequest request,
        CancellationToken cancellationToken)
    {
        var sellerId = await GetCurrentSellerIdAsync(cancellationToken);
        var updated = await _sellerService.UpdateListingPriceAsync(sellerId, id, request.Price, cancellationToken);
        if (updated is null)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Phase 10 (R49) — ثبت موجودی روی یک پیشنهاد فروش.
    ///
    /// چرا policy «CanManageCatalog» اینجا کافی نیست: آن policy فقط می‌گوید
    /// «اجازهٔ نوشتن کاتالوگ دارد»، نه «این Listing مال اوست». یک فروشنده
    /// باید فقط روی Listing خودش موجودی بگذارد، و این تنها با بررسی مالکیت
    /// در سرویس قابل تضمین است. برای همین اینجا [Authorize] ساده است و
    /// کنترل واقعی در InventoryService.AddStockAsync انجام می‌شود.
    /// </summary>
    [HttpPost("{id:int}/inventory")]
    [Authorize]
    [ProducesResponseType(typeof(InventoryStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryStockDto>> AddStock(
        int id,
        [FromBody] AddStockDto dto,
        CancellationToken cancellationToken)
        => Ok(await _inventoryService.AddStockAsync(
            id, dto.WarehouseId, dto.Quantity, cancellationToken));
}