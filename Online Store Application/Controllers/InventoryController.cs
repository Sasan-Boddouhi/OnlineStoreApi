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

    [HttpPost]
    public async Task<IActionResult> CreateInventory([FromBody] CreateInventoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _inventoryService.CreateInventoryAsync(dto.ListingId, dto.WarehouseId, dto.InitialQuantity);
        return CreatedAtAction(nameof(GetAvailable), new { listingId = result.ListingId }, result);
    }

    [HttpGet("available/{listingId:int}")]
    public async Task<IActionResult> GetAvailable(int listingId)
    {
        var available = await _inventoryService.GetAvailableAsync(listingId);
        return Ok(new { ListingId = listingId, AvailableQuantity = available });
    }

    [HttpPost("add-stock/{listingId:int}")]
    public async Task<IActionResult> AddStock(int listingId, [FromBody] AddStockDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _inventoryService.AddStockAsync(listingId, dto.WarehouseId, dto.Quantity);
        return Ok(result);
    }
}

public class CreateInventoryDto
{
    public int ListingId { get; set; }
    public int WarehouseId { get; set; }
    public decimal InitialQuantity { get; set; }
}