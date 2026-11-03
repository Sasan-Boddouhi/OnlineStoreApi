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

    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now.Subtract(_orderTimeout);

            var staleOrders = await unitOfWork.Repository<Order>()
                .ListAsync(
                    new Spec<Order>()
                        .Where(o => o.Status == OrderStatus.Pending && o.OrderDate < cutoff)
                        .Include(o => o.OrderItems)
                        .Include(o => o.SellerGroups)
                        .Include(o => o.Payments)
                        .AsTracking(),
                    cancellationToken);

            if (staleOrders.Count == 0)
                return 0;

            _logger.LogInformation("Found {Count} abandoned pending orders to cancel.", staleOrders.Count);

            foreach (var order in staleOrders)
            {
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

                unitOfWork.Repository<Order>().Update(order);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("AbandonedOrderCleanupService cancelled {Count} abandoned orders and released inventory.", staleOrders.Count);
            return staleOrders.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during AbandonedOrderCleanupService execution.");
            return 0;
        }
    }
}
