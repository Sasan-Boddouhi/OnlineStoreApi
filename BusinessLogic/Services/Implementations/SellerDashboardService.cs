using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 7 — داشبورد فروشنده.
///
/// اصول طراحی:
/// 1) هیچ عددی cache نمی‌شود؛ همه از دادهٔ واقعی سفارش‌ها محاسبه می‌شود تا کهنه نشود.
/// 2) فقط سفارش‌های لغوشده/مرجوعی از فروش کنار گذاشته می‌شوند.
/// 3) دسترسی در سطح سرویس بررسی می‌شود، نه فقط در کنترلر — همان درس فاز ۴ (IDOR).
/// </summary>
public sealed class SellerDashboardService : ISellerDashboardService
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SellerDashboardService> _logger;

    public SellerDashboardService(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ILogger<SellerDashboardService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<SellerSalesSummaryDto> GetSalesSummaryAsync(
        int sellerId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessAsync(sellerId, cancellationToken);

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new BusinessException("بازهٔ زمانی نامعتبر است.", "INVALID_DATE_RANGE");

        // Phase 8 (R35): وقتی بازه داده می‌شود، فیلتر روی DeliveredAt است.
        // تفاوت مهم است: سفارشی که ماه پیش ثبت و این ماه تحویل شده،
        // در گزارش «فروش این ماه» باید بیاید — و با OrderDate نمی‌آمد.
        var filtered = from.HasValue || to.HasValue;
        var start = from ?? DateTime.MinValue;
        var end = to ?? DateTime.MaxValue;

        // فقط سفارش‌های زنده در فروش حساب می‌شوند.
        var groups = await _unitOfWork.Repository<OrderSellerGroup>()
            .ListAsync(
                new Spec<OrderSellerGroup>()
                    .Where(g => g.SellerId == sellerId
                             && g.Order.Status != OrderStatus.Cancelled
                             && g.Order.Status != OrderStatus.Returned),
                cancellationToken);

        if (filtered)
        {
            // تحویل‌نشده‌ها DeliveredAt ندارند و در بازهٔ تحویل نمی‌گنجند.
            groups = groups
                .Where(g => g.DeliveredAt.HasValue
                         && g.DeliveredAt.Value >= start
                         && g.DeliveredAt.Value <= end)
                .ToList();
        }

        // Phase 8 (R37): داشبورد باید بگوید چقدر پرداخت شده و چقدر مانده.
        // این دو عدد از SellerPayout می‌آید، نه از سفارش‌ها، چون تسویه منبع حقیقت است.
        var payouts = await _unitOfWork.Repository<SellerPayout>()
            .ListAsync(
                new Spec<SellerPayout>().Where(p => p.SellerId == sellerId),
                cancellationToken);

        var summary = new SellerSalesSummaryDto
        {
            SellerId = sellerId,
            From = from,
            To = to,
            TotalSales = groups.Sum(g => g.SubTotal),
            TotalOrders = groups.Select(g => g.OrderId).Distinct().Count(),
            CommissionPaid = groups.Sum(g => g.CommissionAmount),
            NetEarnings = groups.Sum(g => g.SellerPayable),
            PaidEarnings = payouts.Where(p => p.Status == PayoutStatus.Paid).Sum(p => p.Amount),
            PendingPayouts = payouts.Where(p => p.Status == PayoutStatus.Pending).Sum(p => p.Amount)
        };

        _logger.LogInformation(
            "Dashboard summary for seller {SellerId}: orders={Orders}, sales={Sales}",
            sellerId, summary.TotalOrders, summary.TotalSales);

        return summary;
    }

    public async Task<PagedResultDto<TopProductDto>> GetTopProductsAsync(
        int sellerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessAsync(sellerId, cancellationToken);

        var (page, size) = NormalizePaging(pageNumber, pageSize);

        // فقط آیتم‌های تحویل‌شده معیار «فروش» هستند.
        var items = await _unitOfWork.Repository<OrderItem>()
            .ListAsync(
                new Spec<OrderItem>()
                    .Where(i => i.Listing.SellerId == sellerId
                             && i.Order.Status == OrderStatus.Delivered),
                cancellationToken);

        var ranked = items
            .GroupBy(i => i.VariantId ?? 0)
            .Select(g => new
            {
                VariantId = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.QuantitySold)
            .ThenByDescending(x => x.Revenue)
            .ToList();

        // Phase 9 (R32): صفحه‌بندی روی مجموعهٔ کامل، سپس برش.
        var totalCount = ranked.Count;
        var pageRows = ranked.Skip((page - 1) * size).Take(size).ToList();

        if (pageRows.Count == 0)
            return new PagedResultDto<TopProductDto>(Array.Empty<TopProductDto>(), page, size, totalCount);

        var variantIds = pageRows.Select(r => r.VariantId).Where(id => id > 0).ToList();

        // نام محصول یک سطح بالاتر از واریانت است، پس Include تک‌سطحی کافی است.
        var variants = variantIds.Count == 0
            ? new List<ProductVariant>()
            : (await _unitOfWork.Repository<ProductVariant>()
                .ListAsync(
                    new Spec<ProductVariant>()
                        .Where(v => variantIds.Contains(v.VariantId))
                        .Include(v => v.Product),
                    cancellationToken)).ToList();

        var rows = pageRows
            .Select(r =>
            {
                var variant = variants.FirstOrDefault(v => v.VariantId == r.VariantId);

                return new TopProductDto
                {
                    VariantId = r.VariantId,
                    ProductId = variant?.ProductId ?? 0,
                    ProductName = variant?.Product?.Name ?? string.Empty,
                    VariantName = variant?.Name,
                    QuantitySold = r.QuantitySold,
                    Revenue = r.Revenue
                };
            })
            .ToList();

        return new PagedResultDto<TopProductDto>(rows, page, size, totalCount);
    }

    public async Task<PagedResultDto<SellerRecentOrderDto>> GetRecentOrdersAsync(
        int sellerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessAsync(sellerId, cancellationToken);

        var (page, size) = NormalizePaging(pageNumber, pageSize);

        var items = await _unitOfWork.Repository<OrderItem>()
            .ListAsync(
                new Spec<OrderItem>()
                    .Where(i => i.Listing.SellerId == sellerId)
                    .Include(i => i.Order),
                cancellationToken);

        var ordered = items
            .OrderByDescending(i => i.Order.OrderDate)
            .ThenByDescending(i => i.OrderItemId)
            .ToList();

        var totalCount = ordered.Count;
        var recent = ordered.Skip((page - 1) * size).Take(size).ToList();

        if (recent.Count == 0)
            return new PagedResultDto<SellerRecentOrderDto>(Array.Empty<SellerRecentOrderDto>(), page, size, totalCount);

        // نام محصول و واریانت در یک کوئری جدا خوانده می‌شود (Spec فقط Include تک‌سطحی دارد).
        var variantIds = recent
            .Select(i => i.VariantId ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var variants = variantIds.Count == 0
            ? new List<ProductVariant>()
            : (await _unitOfWork.Repository<ProductVariant>()
                .ListAsync(
                    new Spec<ProductVariant>()
                        .Where(v => variantIds.Contains(v.VariantId))
                        .Include(v => v.Product),
                    cancellationToken)).ToList();

        var rows = recent
            .Select(i =>
            {
                var variant = variants.FirstOrDefault(v => v.VariantId == i.VariantId);

                return new SellerRecentOrderDto
                {
                    OrderId = i.OrderId,
                    OrderItemId = i.OrderItemId,
                    OrderDate = i.Order.OrderDate,
                    OrderStatus = i.Order.Status.ToString(),
                    ProductName = variant?.Product?.Name ?? string.Empty,
                    VariantName = variant?.Name,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    LineTotal = i.TotalPrice
                };
            })
            .ToList();

        return new PagedResultDto<SellerRecentOrderDto>(rows, page, size, totalCount);
    }

    /// <summary>
    /// صفحه و اندازهٔ صفحه را در بازهٔ مجاز نگه می‌دارد.
    /// سقف لازم است تا کسی با pageSize=1000000 سرور را زمین نزند.
    /// </summary>
    private static (int Page, int Size) NormalizePaging(int pageNumber, int pageSize)
    {
        var page = pageNumber <= 0 ? 1 : pageNumber;
        var size = pageSize <= 0 ? DefaultLimit : Math.Min(pageSize, MaxLimit);

        return (page, size);
    }

    /// <summary>
    /// ادمین همه را می‌بیند؛ فروشنده فقط دادهٔ خودش.
    /// بررسی در سرویس انجام می‌شود تا حتی اگر کنترلر جدیدی اضافه شد، محافظت از دست نرود.
    /// </summary>
    private async Task EnsureCanAccessAsync(int sellerId, CancellationToken cancellationToken)
    {
        var role = _currentUser.GetCurrentUserRole();

        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return;

        var userId = _currentUser.TryGetCurrentUserId();
        if (userId is null)
            throw new ForbiddenException("دسترسی به داشبورد فروشنده مجاز نیست.", "DASHBOARD_FORBIDDEN");

        var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);

        if (seller is null)
            throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

        if (seller.UserId != userId.Value)
            throw new ForbiddenException("این داشبورد متعلق به فروشندهٔ دیگری است.", "DASHBOARD_FORBIDDEN");
    }
}