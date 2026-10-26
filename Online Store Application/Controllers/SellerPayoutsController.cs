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

    [HttpGet("my")]
    public async Task<IActionResult> GetMy(CancellationToken cancellationToken = default)
    {
        var payouts = await _payoutService.GetMyPayoutsAsync(cancellationToken);
        return Ok(payouts);
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
    {
        var payouts = await _payoutService.GetAllPayoutsAsync(cancellationToken);
        return Ok(payouts);
    }

    [HttpPost("{id:int}/pay")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<SellerPayoutDto>> Pay(
        int id,
        [FromBody] MarkPayoutPaidRequest? request,
        CancellationToken cancellationToken = default)
    {
        var payout = await _payoutService.MarkAsPaidAsync(id, request?.Reference, cancellationToken);
        return Ok(payout);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<SellerPayoutDto>> Cancel(
        int id,
        CancellationToken cancellationToken = default)
    {
        var payout = await _payoutService.CancelPayoutAsync(id, cancellationToken);
        return Ok(payout);
    }
}
