using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/coupons")]
[Authorize(Policy = "AdminOnly")]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _couponService;

    public CouponsController(ICouponService couponService)
    {
        _couponService = couponService;
    }

    /// <summary>دریافت تمام کدهای تخفیف (مخصوص ادمین).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CouponDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CouponDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await _couponService.GetAllAsync(cancellationToken));

    /// <summary>دریافت مشخصات کوپن بر اساس شناسه (مخصوص ادمین).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _couponService.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>ایجاد کد تخفیف جدید توسط ادمین.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CouponDto>> Create([FromBody] CreateCouponDto dto, CancellationToken cancellationToken)
    {
        var created = await _couponService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.CouponId }, created);
    }

    /// <summary>ویرایش کد تخفیف توسط ادمین.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> Update(int id, [FromBody] UpdateCouponDto dto, CancellationToken cancellationToken)
    {
        var updated = await _couponService.UpdateAsync(id, dto, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    /// <summary>حذف (غیرفعال‌سازی) کد تخفیف توسط ادمین.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _couponService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
