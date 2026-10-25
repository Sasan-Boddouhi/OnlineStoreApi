using BusinessLogic.DTOs.Warehouse;
using BusinessLogic.Services.Interfaces;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ICurrentUserService _currentUserService;

    public WarehousesController(IWarehouseService warehouseService, ICurrentUserService currentUserService)
    {
        _warehouseService = warehouseService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var warehouses = await _warehouseService.GetAllAsync();
        return Ok(warehouses);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var warehouse = await _warehouseService.GetByIdAsync(id);
        if (warehouse == null) return NotFound();
        return Ok(warehouse);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWarehouseDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        
        var userId = _currentUserService.GetCurrentUserId();
        var warehouse = await _warehouseService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = warehouse.WarehouseId }, warehouse);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWarehouseDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != dto.WarehouseId) return BadRequest("ID mismatch");

        var warehouse = await _warehouseService.UpdateAsync(id, dto);
        if (warehouse == null) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _currentUserService.GetCurrentUserId();
        var deleted = await _warehouseService.DeleteAsync(id, userId);
        if (!deleted) return NotFound();
        return NoContent();
    }
}