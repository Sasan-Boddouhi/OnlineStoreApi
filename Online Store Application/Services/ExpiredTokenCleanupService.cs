using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Online_Store_Application.Services;

/// <summary>
/// M7.7 — پاک‌سازی روزانهٔ توکن‌های تازه‌سازی منقضی و لغوشده (بیش از ۳۰ روز از انقضا گذشته).
/// </summary>
public sealed class ExpiredTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredTokenCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public ExpiredTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpiredTokenCleanupService started with interval {Interval}.", _interval);

        using var timer = new PeriodicTimer(_interval);

        do
        {
            await RunCleanupAsync(stoppingToken);
            await RunOtpCleanupAsync(stoppingToken);
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
    /// Batch 46: کدهای OTP پس از ۲ روز دیگر برای هیچ سقفی (ساعتی، روزانه) لازم نیستند و حذف می‌شوند (فقط هش بودند).
    /// </summary>
    public async Task<int> RunOtpCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = unitOfWork.Repository<OtpCode>();

            var threshold = DateTime.UtcNow.AddDays(-2);
            var staleCodes = await repository.ListAsync(
                new Spec<OtpCode>().Where(o => o.CreatedAtUtc < threshold).AsTracking(), cancellationToken);

            if (staleCodes.Count == 0)
                return 0;

            repository.DeleteRange(staleCodes);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("ExpiredTokenCleanupService purged {Count} old OTP codes.", staleCodes.Count);
            return staleCodes.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while purging old OTP codes.");
            return 0;
        }
    }

    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var threshold = DateTime.UtcNow.AddDays(-30);

            var staleTokens = await unitOfWork.Repository<RefreshTokenEntity>()
                .ListAsync(
                    new Spec<RefreshTokenEntity>()
                        .Where(t => t.IsRevoked && t.ExpiryDate < threshold)
                        .AsTracking(),
                    cancellationToken);

            if (staleTokens.Count == 0)
                return 0;

            // رفع وابستگی خودارجاعی قبل از حذف
            foreach (var token in staleTokens)
            {
                token.ReplacedByTokenId = null;
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);

            unitOfWork.Repository<RefreshTokenEntity>().DeleteRange(staleTokens);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("ExpiredTokenCleanupService purged {Count} revoked/expired tokens.", staleTokens.Count);
            return staleTokens.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during ExpiredTokenCleanupService execution.");
            return 0;
        }
    }
}
