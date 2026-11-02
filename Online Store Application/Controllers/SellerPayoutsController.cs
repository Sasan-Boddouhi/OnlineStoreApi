using Asp.Versioning;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/seller-payouts")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class SellerPayoutsController : ControllerBase
{
    private readonly ISellerPayoutService _payoutService;

    public SellerPayoutsController(ISellerPayoutService payoutService)
    {
        _payoutService = payoutService;
    }

    /// <summary>دریافت تسویه‌حساب‌های فروشنده جاری.</summary>
    [HttpGet("my")]
    [ProducesResponseType(typeof(IEnumerable<SellerPayoutDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMy(CancellationToken cancellationToken = default)
    {
        var payouts = await _payoutService.GetMyPayoutsAsync(cancellationToken);
        return Ok(payouts);
    }

    /// <summary>دریافت تمام تسویه‌حساب‌ها (مخصوص ادمین).</summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IEnumerable<SellerPayoutDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
    {
        var payouts = await _payoutService.GetAllPayoutsAsync(cancellationToken);
        return Ok(payouts);
    }

    /// <summary>ثبت پرداخت و تسویه حساب توسط ادمین.</summary>
    [HttpPost("{id:int}/pay")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SellerPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerPayoutDto>> Pay(
        int id,
        [FromBody] MarkPayoutPaidRequest? request,
        CancellationToken cancellationToken = default)
    {
        var payout = await _payoutService.MarkAsPaidAsync(id, request?.Reference, cancellationToken);
        return Ok(payout);
    }

    /// <summary>لغو تسویه‌حساب توسط ادمین.</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SellerPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerPayoutDto>> Cancel(
        int id,
        CancellationToken cancellationToken = default)
    {
        var payout = await _payoutService.CancelPayoutAsync(id, cancellationToken);
        return Ok(payout);
    }
}
