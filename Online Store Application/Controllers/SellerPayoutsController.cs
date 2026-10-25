using Asp.Versioning;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
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
}
