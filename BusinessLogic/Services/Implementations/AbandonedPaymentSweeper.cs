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

        // Batch 5 (F-07): Verify این پرداخت‌ها آغاز شده (Payment.VerifyStartedAtUtc، Batch 41) و نتیجه‌اش نامعلوم است؛ شاید پول برداشته شده باشد.
        // لغو نمی‌شوند، فقط یک بار برای بررسی دستی ثبت می‌شوند. تأیید دوباره وضعیت را قطعی می‌کند.
        var unresolved = PaymentLifecycle.FindUnresolvedVerifications(stale);
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

        // Batch 42 (FIN-12): «یک بار» از ستون خودِ پرداخت خوانده می‌شود، نه از AuditLog. بررسیِ بسته‌شده هم دوباره
        // باز نمی‌شود: ادمین آن را دیده و تصمیم گرفته است (مسئلهٔ تازه از مسیر برداشت دوباره بازش می‌کند).
        if (payment.ReviewRequiredAtUtc is not null)
            return;

        var details =
            $"source=abandoned-payment sweep | order={payment.OrderId} | amount={payment.Amount} | paymentState={payment.State} " +
            "| issues: the payment's verification outcome is unknown (verify started, no definitive gateway answer); the sweep skipped it — verify the payment again to resolve it";

        payment.FlagForReview(_timeProvider.GetUtcNow().UtcDateTime, details);
        _unitOfWork.Repository<Payment>().Update(payment);

        await _auditService.RecordAsync(
            PaymentLifecycle.ReviewRequiredAction,
            nameof(Payment),
            payment.PaymentId.ToString(),
            details,
            cancellationToken);
    }
}
