using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/shipping-methods")]
public class ShippingMethodsController : ControllerBase
{
    private readonly IShippingMethodService _shippingMethodService;

    public ShippingMethodsController(IShippingMethodService shippingMethodService)
    {
        _shippingMethodService = shippingMethodService;
    }

    /// <summary>دریافت فهرست روش‌های ارسال فعال.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<ShippingMethodDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ShippingMethodDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await _shippingMethodService.GetAllAsync(cancellationToken));

    /// <summary>دریافت مشخصات یک روش ارسال بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ShippingMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShippingMethodDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _shippingMethodService.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>ایجاد روش ارسال جدید توسط ادمین.</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ShippingMethodDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ShippingMethodDto>> Create([FromBody] CreateShippingMethodDto dto, CancellationToken cancellationToken)
    {
        var created = await _shippingMethodService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.ShippingMethodId }, created);
    }

    /// <summary>ویرایش روش ارسال توسط ادمین.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ShippingMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShippingMethodDto>> Update(int id, [FromBody] UpdateShippingMethodDto dto, CancellationToken cancellationToken)
    {
        var updated = await _shippingMethodService.UpdateAsync(id, dto, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    /// <summary>حذف روش ارسال توسط ادمین.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _shippingMethodService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
