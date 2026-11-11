using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 5 — قواعد مشترک چرخهٔ عمر پرداخت که PaymentService، لغو سفارش (OrderService) و جاب سفارش‌های
/// رهاشده (AbandonedOrderCleanupService) باید یکسان اجرا کنند. ایستا و فقط وابسته به IUnitOfWork و
/// IAuditService است تا OrderService به PaymentService وابسته نشود (چرخهٔ DI).
///
/// «تأیید آغازشده»: پیش از فراخوانی Verify درگاه، رکورد <see cref="VerificationStartedAction"/> در AuditLog
/// ثبت می‌شود. از آن لحظه تا وقتی پرداخت Pending است، ممکن است درگاه پول را برداشته باشد (مثلاً پاسخ
/// Verify در timeout گم شده)؛ چنین پرداختی هرگز بی‌صدا لغو نمی‌شود، وگرنه پولِ برداشته‌شده بی‌ردپا می‌ماند.
/// </summary>
public static class PaymentLifecycle
{
    public const string VerificationStartedAction = "Payment.VerificationStarted";
    public const string VerificationPendingAction = "Payment.VerificationPending";
    public const string CancelledAction = "Payment.Cancelled";
    public const string NotVerifiedAction = "Payment.NotVerified";
    public const string ReviewRequiredAction = "Payment.ReviewRequired";

    /// <summary>پرداختی که هنوز ممکن است پول بگیرد.</summary>
    public static bool IsOpen(Payment payment)
        => payment.State is PaymentState.Initiated or PaymentState.Pending;

    /// <summary>
    /// شناسهٔ پرداخت‌های Pending که Verify آن‌ها آغاز شده و نتیجه‌اش هنوز ثبت نشده است.
    /// </summary>
    public static async Task<HashSet<int>> FindUnresolvedVerificationsAsync(
        IUnitOfWork unitOfWork,
        IEnumerable<Payment> payments,
        CancellationToken cancellationToken)
    {
        var pendingIds = payments
            .Where(p => p.State == PaymentState.Pending)
            .Select(p => p.PaymentId.ToString())
            .ToList();

        if (pendingIds.Count == 0)
            return new HashSet<int>();

        var entries = await unitOfWork.Repository<AuditLog>().ListAsync(
            new Spec<AuditLog>().Where(a => a.Action == VerificationStartedAction
                                         && a.EntityName == nameof(Payment)
                                         && pendingIds.Contains(a.EntityId!)),
            cancellationToken);

        return entries.Select(a => int.Parse(a.EntityId!)).ToHashSet();
    }

    /// <summary>
    /// پرداخت‌های باز (Initiated/Pending) را با دلیل مشخص لغو و در AuditLog ثبت می‌کند. پرداخت‌ها باید ردیابی
    /// شده باشند؛ ذخیره با فراخوان است. پولی که مشتری برای Authority لغوشده پرداخته باشد، چون Verify نمی‌شود،
    /// طبق مستند زرین‌پال به حساب خریدار برمی‌گردد.
    /// </summary>
    public static async Task<int> CancelOpenPaymentsAsync(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        IEnumerable<Payment> payments,
        string reason,
        CancellationToken cancellationToken)
    {
        var cancelled = 0;

        foreach (var payment in payments.Where(IsOpen))
        {
            payment.TransitionTo(PaymentState.Cancelled, reason);
            unitOfWork.Repository<Payment>().Update(payment);
            await auditService.RecordAsync(
                CancelledAction,
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"order={payment.OrderId} | reason={reason} | not verified: the gateway returns any unverified payment to the payer",
                cancellationToken);
            cancelled++;
        }

        return cancelled;
    }
}
