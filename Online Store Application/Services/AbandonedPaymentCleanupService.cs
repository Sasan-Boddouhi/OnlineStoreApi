using Application.Options;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Services;

/// <summary>
/// Phase 9 (R44) — اجرای دوره‌ای پاک‌سازی پرداخت‌های رهاشده.
///
/// منطق در <see cref="IAbandonedPaymentSweeper"/> است و این کلاس فقط زمان‌بندی می‌کند،
/// تا تست‌ها بدون بلند کردن هاست و بدون انتظار واقعی اجرا شوند.
///
/// هر خطا داخل حلقه گرفته می‌شود: یک بار خرابی دیتابیس نباید job را برای همیشه بکشد.
/// </summary>
public sealed class AbandonedPaymentCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AbandonedPaymentSettings _settings;
    private readonly ILogger<AbandonedPaymentCleanupService> _logger;

    public AbandonedPaymentCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<AbandonedPaymentSettings> settings,
        ILogger<AbandonedPaymentCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Abandoned payment cleanup is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.ScanIntervalMinutes));

        _logger.LogInformation(
            "Abandoned payment cleanup started: interval={Interval}, timeout={Timeout}",
            interval, _settings.TimeoutMinutes);

        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunOnceAsync(stoppingToken);
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

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<IAbandonedPaymentSweeper>();

            var count = await sweeper.SweepAsync(stoppingToken);

            if (count > 0)
                _logger.LogWarning("Abandoned payment cleanup cancelled {Count} payments.", count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // خاموش‌شدن عادی سرویس.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abandoned payment cleanup iteration failed.");
        }
    }
}