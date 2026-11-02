using Asp.Versioning;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 7 — داشبورد فروشنده.
///
/// کنترلر عمداً thin است؛ بررسی مالکیت داده در SellerDashboardService انجام می‌شود
/// تا با اضافه شدن endpoint جدید هم از دست نرود.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/sellers/{sellerId:int}/dashboard")]
[Authorize]
public class SellerDashboardController : ControllerBase
{
    private readonly ISellerDashboardService _dashboardService;

    public SellerDashboardController(ISellerDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>دریافت خلاصه فروش و آمارهای کلیدی فروشنده.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SellerSalesSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerSalesSummaryDto>> GetSummary(
        int sellerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
        => Ok(await _dashboardService.GetSalesSummaryAsync(sellerId, from, to, cancellationToken));

    // Phase 9 (R32): صفحه‌بندی به‌جای «limit».
    // limit یک برش بی‌فراداده بود: کلاینت نمی‌دانست چند صفحه وجود دارد.
    /// <summary>دریافت محصولات پرفروش فروشنده با صفحه‌بندی.</summary>
    [HttpGet("top-products")]
    [ProducesResponseType(typeof(PagedResult<TopProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TopProductDto>>> GetTopProducts(
        int sellerId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
        => Ok(await _dashboardService.GetTopProductsAsync(sellerId, pageNumber, pageSize, cancellationToken));

    /// <summary>دریافت آخرین سفارش‌های فروشنده با صفحه‌بندی.</summary>
    [HttpGet("recent-orders")]
    [ProducesResponseType(typeof(PagedResult<SellerRecentOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<SellerRecentOrderDto>>> GetRecentOrders(
        int sellerId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
        => Ok(await _dashboardService.GetRecentOrdersAsync(sellerId, pageNumber, pageSize, cancellationToken));
}