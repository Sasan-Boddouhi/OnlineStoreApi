using System.Security.Claims;
using Application.Common.Constants;
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
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ISellerService _sellerService;
    private readonly ICurrentUserService _currentUserService;

    public WarehousesController(
        IWarehouseService warehouseService,
        ISellerService sellerService,
        ICurrentUserService currentUserService)
    {
        _warehouseService = warehouseService;
        _sellerService = sellerService;
        _currentUserService = currentUserService;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedId))
        {
            return parsedId;
        }

        return _currentUserService.GetCurrentUserId();
    }

    private bool IsAdmin()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.IsInRole(RoleConstants.Admin) || User.IsInRole(RoleConstants.SuperAdmin) ||
                   User.Claims.Any(c => (c.Type == ClaimTypes.Role || c.Type == "role") &&
                                        (string.Equals(c.Value, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(c.Value, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase)));
        }

        var role = _currentUserService.GetCurrentUserRole();
        return string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? sellerId, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
        {
            var userId = GetCurrentUserId();
            var seller = await _sellerService.GetByUserIdAsync(userId, cancellationToken);
            if (seller == null)
            {
                return Ok(Array.Empty<WarehouseDto>());
            }

            sellerId = seller.SellerId;
        }

        var warehouses = await _warehouseService.GetAllAsync(sellerId, cancellationToken);
        return Ok(warehouses);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouseService.GetByIdAsync(id, cancellationToken);
        if (warehouse == null) return NotFound();

        if (!IsAdmin())
        {
            var userId = GetCurrentUserId();
            var seller = await _sellerService.GetByUserIdAsync(userId, cancellationToken);
            if (seller == null || warehouse.SellerId != seller.SellerId)
                return Forbid();
        }

        return Ok(warehouse);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWarehouseDto dto, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();

        if (!IsAdmin())
        {
            var seller = await _sellerService.GetByUserIdAsync(userId, cancellationToken);
            if (seller != null)
            {
                dto.SellerId = seller.SellerId;
            }
        }

        var warehouse = await _warehouseService.CreateAsync(dto, userId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = warehouse.WarehouseId }, warehouse);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWarehouseDto dto, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != dto.WarehouseId) return BadRequest("ID mismatch");

        var userId = GetCurrentUserId();
        var warehouse = await _warehouseService.UpdateAsync(userId, id, dto, cancellationToken);
        if (warehouse == null) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var deleted = await _warehouseService.DeleteAsync(id, userId, cancellationToken);
        if (!deleted) return NotFound();
        return NoContent();
    }
}