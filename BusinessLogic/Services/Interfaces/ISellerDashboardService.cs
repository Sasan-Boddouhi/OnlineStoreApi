using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Phase 7 — داشبورد فروشنده.
/// همهٔ متدها sellerId می‌گیرند و خودشان دسترسی را بررسی می‌کنند
/// (فروشنده فقط دادهٔ خودش، ادمین همه).
/// </summary>
public interface ISellerDashboardService
{
    Task<SellerSalesSummaryDto> GetSalesSummaryAsync(
        int sellerId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<TopProductDto>> GetTopProductsAsync(
        int sellerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<SellerRecentOrderDto>> GetRecentOrdersAsync(
        int sellerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}