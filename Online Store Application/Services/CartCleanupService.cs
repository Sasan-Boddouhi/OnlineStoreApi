using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Online_Store_Application.Services;

/// <summary>
/// M7.7 — پاک‌سازی دوره‌ای سبدهای خرید رهاشدهٔ مهمان (بدون کاربر و با آخرین تغییر بیش از ۷ روز پیش).
/// </summary>
public sealed class CartCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CartCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public CartCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<CartCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CartCleanupService started with interval {Interval}.", _interval);

        using var timer = new PeriodicTimer(_interval);

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

    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var threshold = DateTime.UtcNow.AddDays(-7);

            var staleCarts = await unitOfWork.Repository<Cart>()
                .ListAsync(
                    new Spec<Cart>()
                        .Where(c => c.UserId == null && c.ModifiedOn < threshold)
                        .Include(c => c.Items)
                        .AsTracking(),
                    cancellationToken);

            if (staleCarts.Count == 0)
                return 0;

            unitOfWork.Repository<Cart>().DeleteRange(staleCarts);
            var deletedCount = await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("CartCleanupService deleted {Count} stale guest carts.", staleCarts.Count);
            return staleCarts.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during CartCleanupService execution.");
            return 0;
        }
    }
}
