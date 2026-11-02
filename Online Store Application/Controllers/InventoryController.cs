using BusinessLogic.DTOs.Listing;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>ایجاد رکورد موجودی انبار برای یک پیشنهاد فروش.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(InventoryStockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateInventory([FromBody] CreateInventoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _inventoryService.CreateInventoryAsync(dto.ListingId, dto.WarehouseId, dto.InitialQuantity);
        return CreatedAtAction(nameof(GetAvailable), new { listingId = result.ListingId }, result);
    }

    /// <summary>دریافت موجودی در دسترس یک پیشنهاد فروش.</summary>
    [HttpGet("available/{listingId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable(int listingId)
    {
        var available = await _inventoryService.GetAvailableAsync(listingId);
        return Ok(new { ListingId = listingId, AvailableQuantity = available });
    }

    /// <summary>افزایش موجودی انبار برای پیشنهاد فروش.</summary>
    [HttpPost("add-stock/{listingId:int}")]
    [ProducesResponseType(typeof(InventoryStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddStock(int listingId, [FromBody] AddStockDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _inventoryService.AddStockAsync(listingId, dto.WarehouseId, dto.Quantity);
        return Ok(result);
    }
}