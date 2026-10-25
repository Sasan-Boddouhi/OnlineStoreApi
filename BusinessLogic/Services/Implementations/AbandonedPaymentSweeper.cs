using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 9 (R44) — پرداخت‌های Pending که کاربر رهایشان کرده، برای همیشه Pending می‌ماندند.
/// نتیجه: آمار «پرداخت در انتظار» متورم و تسویه‌های نامفهوم.
///
/// این کلاس فقط پرداخت‌هایی را می‌بندد که درگاه هیچ‌گاه تأییدشان نکرده است.
/// سفارش دست‌نخورده می‌ماند تا کاربر دوباره تلاش کند (تصمیم فاز ۸).
/// </summary>
public sealed class AbandonedPaymentSweeper : IAbandonedPaymentSweeper
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly AbandonedPaymentSettings _settings;
    private readonly ILogger<AbandonedPaymentSweeper> _logger;

    public AbandonedPaymentSweeper(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        TimeProvider timeProvider,
        IOptions<AbandonedPaymentSettings> settings,
        ILogger<AbandonedPaymentSweeper> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now.AddMinutes(-Math.Max(1, _settings.TimeoutMinutes));

        var stale = await _unitOfWork.Repository<Payment>()
            .ListAsync(
                new Spec<Payment>()
                    .Where(p => p.State == PaymentState.Pending && p.LastStateChangedAtUtc < cutoff)
                    .AsTracking(),
                cancellationToken);

        if (stale.Count == 0)
            return 0;

        foreach (var payment in stale)
        {
            payment.TransitionTo(PaymentState.Cancelled, "abandoned: no gateway confirmation");

            await _auditService.RecordAsync(
                "Payment.Cancelled",
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"abandoned after {_settings.TimeoutMinutes} minutes",
                cancellationToken);

            _unitOfWork.Repository<Payment>().Update(payment);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Cancelled {Count} abandoned payments older than {Cutoff:u}", stale.Count, cutoff);

        return stale.Count;
    }
}