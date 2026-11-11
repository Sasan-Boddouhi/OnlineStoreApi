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

        // Batch 5 (F-07): Verify این پرداخت‌ها آغاز شده و نتیجه‌اش نامعلوم است؛ شاید پول برداشته شده باشد.
        // لغو نمی‌شوند، فقط یک بار برای بررسی دستی ثبت می‌شوند. تأیید دوباره وضعیت را قطعی می‌کند.
        var unresolved = await PaymentLifecycle.FindUnresolvedVerificationsAsync(_unitOfWork, stale, cancellationToken);
        foreach (var payment in stale.Where(p => unresolved.Contains(p.PaymentId)))
            await FlagUnresolvedOnceAsync(payment, cancellationToken);

        var cancelled = 0;
        foreach (var payment in stale.Where(p => !unresolved.Contains(p.PaymentId)))
        {
            payment.TransitionTo(PaymentState.Cancelled, "abandoned: no gateway confirmation");

            await _auditService.RecordAsync(
                "Payment.Cancelled",
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"abandoned after {_settings.TimeoutMinutes} minutes",
                cancellationToken);

            _unitOfWork.Repository<Payment>().Update(payment);
            cancelled++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Cancelled {Count} abandoned payments older than {Cutoff:u}", cancelled, cutoff);

        return cancelled;
    }

    private async Task FlagUnresolvedOnceAsync(Payment payment, CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Payment {PaymentId} is Pending with an unknown verification outcome; the abandoned-payment sweep skipped it. Needs manual review.",
            payment.PaymentId);

        var paymentKey = payment.PaymentId.ToString();
        var alreadyFlagged = await _unitOfWork.Repository<AuditLog>().AnyAsync(
            new Spec<AuditLog>().Where(a => a.Action == PaymentLifecycle.ReviewRequiredAction
                                         && a.EntityName == nameof(Payment)
                                         && a.EntityId == paymentKey),
            cancellationToken);

        if (alreadyFlagged)
            return;

        await _auditService.RecordAsync(
            PaymentLifecycle.ReviewRequiredAction,
            nameof(Payment),
            paymentKey,
            $"source=abandoned-payment sweep | order={payment.OrderId} | amount={payment.Amount} | paymentState={payment.State} " +
            "| issues: the payment's verification outcome is unknown (verify started, no definitive gateway answer); the sweep skipped it — verify the payment again to resolve it",
            cancellationToken);
    }
}
