using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Online_Store_Application.Services;

/// <summary>
/// M7.9 — جاب پس‌زمینه برای لغو خودکار سفارش‌های در انتظار رهاشده و آزادسازی موجودی رزروشده.
/// اجرای هر ۱۰ دقیقه؛ بررسی سفارش‌های Pending با بیش از ۶۰ دقیقه عمر.
/// </summary>
public sealed class AbandonedOrderCleanupService : BackgroundService
{
    private const string ReviewRequiredAction = "Payment.ReviewRequired";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbandonedOrderCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _orderTimeout = TimeSpan.FromMinutes(60);

    public AbandonedOrderCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedOrderCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AbandonedOrderCleanupService started with interval {Interval}.", _interval);

        using var timer = new PeriodicTimer(_interval, _timeProvider);

        do
        {
            await RunCleanupAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Batch 4 (F-18): هر سفارش در scope و SaveChanges جداگانهٔ خودش لغو می‌شود.
    /// پیش‌تر یک سفارش خراب (مثلاً گروهِ «تحویل‌شده» در سفارش Pending) کل دسته را
    /// برمی‌گرداند و هیچ موجودی رزروشده‌ای آزاد نمی‌شد. اکنون آن سفارش ثبت خطا و رد
    /// می‌شود و بقیه پردازش می‌شوند.
    /// </summary>
    /// <returns>تعداد سفارش‌هایی که واقعاً لغو و ذخیره شدند.</returns>
    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        List<int> staleOrderIds;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.Subtract(_orderTimeout);

            staleOrderIds = (await unitOfWork.Repository<Order>()
                    .ListAsync(
                        new Spec<Order>().Where(o => o.Status == OrderStatus.Pending && o.OrderDate < cutoff),
                        cancellationToken))
                .Select(o => o.OrderId)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AbandonedOrderCleanupService could not list abandoned orders.");
            return 0;
        }

        if (staleOrderIds.Count == 0)
            return 0;

        _logger.LogInformation("Found {Count} abandoned pending orders to cancel.", staleOrderIds.Count);

        var cancelled = 0;
        var failed = 0;

        foreach (var orderId in staleOrderIds)
        {
            try
            {
                if (await CancelAbandonedOrderAsync(orderId, cancellationToken))
                    cancelled++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "AbandonedOrderCleanupService could not cancel order {OrderId}; it was skipped and the remaining orders continue.",
                    orderId);
            }
        }

        _logger.LogWarning(
            "AbandonedOrderCleanupService cancelled {Cancelled} abandoned orders and released inventory; {Failed} failed.",
            cancelled, failed);

        return cancelled;
    }

    /// <summary>
    /// یک سفارش رهاشده را در scope خودش لغو و ذخیره می‌کند.
    /// اگر سفارش در این فاصله پرداخت/تغییر کرده باشد، کاری نمی‌کند.
    /// </summary>
    private async Task<bool> CancelAbandonedOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        var order = await unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>()
                    .Where(o => o.OrderId == orderId)
                    .Include(o => o.OrderItems)
                    .Include(o => o.SellerGroups)
                    .Include(o => o.Payments)
                    .AsTracking(),
                cancellationToken);

        if (order is null || order.Status != OrderStatus.Pending)
            return false;

        // Batch 4c: پرداخت پیش از به‌روزرسانی سفارش Succeeded ذخیره می‌شود (Batch 4b)؛ اگر آن گام
        // شکسته باشد، سفارش با پرداخت موفق Pending می‌ماند. چنین سفارشی هرگز لغو نمی‌شود و موجودی‌اش
        // آزاد نمی‌شود (پولش گرفته شده)؛ فقط برای بررسی دستی ثبت می‌شود.
        var succeededPayment = order.Payments.FirstOrDefault(p => p.State == PaymentState.Succeeded);
        if (succeededPayment is not null)
        {
            await FlagPaidPendingOrderAsync(
                unitOfWork,
                scope.ServiceProvider.GetRequiredService<IAuditService>(),
                order,
                succeededPayment,
                cancellationToken);
            return false;
        }

        order.Cancel();

        foreach (var group in order.SellerGroups)
        {
            group.Cancel();
        }

        foreach (var item in order.OrderItems)
        {
            await inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);
        }

        foreach (var payment in order.Payments)
        {
            if (payment.State is PaymentState.Pending or PaymentState.Initiated)
            {
                payment.TransitionTo(PaymentState.Cancelled, "abandoned: order cancelled by timeout");
                unitOfWork.Repository<Payment>().Update(payment);
            }
        }

        // M7.13: Restore coupon quota if abandoned order is cancelled
        if (order.CouponId.HasValue && !order.CouponQuotaRefunded)
        {
            var coupon = await unitOfWork.Repository<Coupon>()
                .FirstOrDefaultAsync(new Spec<Coupon>().Where(c => c.CouponId == order.CouponId.Value).AsTracking(), cancellationToken);

            if (coupon != null && coupon.UsedCount > 0)
            {
                coupon.UsedCount--;
                unitOfWork.Repository<Coupon>().Update(coupon);
            }

            order.CouponQuotaRefunded = true;
        }

        unitOfWork.Repository<Order>().Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Batch 4c: سفارش Pending با پرداخت Succeeded را به‌جای لغو، با عنوان Payment.ReviewRequired
    /// در AuditLog ثبت می‌کند. جاب هر ۱۰ دقیقه اجرا می‌شود، پس اگر برای همان پرداخت قبلاً
    /// (اینجا یا در مسیر verify/webhook) مورد بررسی دستی ثبت شده باشد، دوباره ثبت نمی‌شود؛
    /// لاگ Error در هر اجرا باقی می‌ماند.
    /// </summary>
    private async Task FlagPaidPendingOrderAsync(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        Order order,
        Payment payment,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Order {OrderId} is Pending but payment {PaymentId} already Succeeded; skipped by the abandoned-order cleanup (no cancel, no stock release). Needs manual review.",
            order.OrderId, payment.PaymentId);

        var paymentKey = payment.PaymentId.ToString();
        var alreadyFlagged = await unitOfWork.Repository<AuditLog>()
            .AnyAsync(
                new Spec<AuditLog>().Where(a => a.Action == ReviewRequiredAction
                                              && a.EntityName == nameof(Payment)
                                              && a.EntityId == paymentKey),
                cancellationToken);

        if (alreadyFlagged)
            return;

        await auditService.RecordAsync(
            ReviewRequiredAction,
            nameof(Payment),
            paymentKey,
            $"source=abandoned-order cleanup | order={order.OrderId} | amount={payment.Amount} " +
            $"| paymentState={payment.State} | issues: order is still Pending although the payment Succeeded; cleanup skipped it",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
