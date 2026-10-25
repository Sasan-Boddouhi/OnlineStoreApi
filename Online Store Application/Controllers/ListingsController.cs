using Asp.Versioning;
using BusinessLogic.DTOs.Listing;
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

    public ListingsController(ISellerService sellerService, IInventoryService inventoryService)
    {
        _sellerService = sellerService;
        _inventoryService = inventoryService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SellerProductDto>>> GetAll(
        [FromQuery] int? sellerId,
        CancellationToken cancellationToken)
        => Ok(await _sellerService.GetListingsAsync(sellerId, cancellationToken));

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<SellerProductDto>> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _sellerService.GetListingAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<ActionResult<SellerProductDto>> Create(
        [FromBody] CreateSellerProductDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _sellerService.CreateListingAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.ListingId }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<ActionResult<SellerProductDto>> Update(
        int id,
        [FromBody] UpdateSellerProductDto dto,
        CancellationToken cancellationToken)
        => Ok(await _sellerService.UpdateListingAsync(id, dto, cancellationToken));

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
    public async Task<ActionResult<InventoryStockDto>> AddStock(
        int id,
        [FromBody] AddStockDto dto,
        CancellationToken cancellationToken)
        => Ok(await _inventoryService.AddStockAsync(
            id, dto.WarehouseId, dto.Quantity, cancellationToken));
}