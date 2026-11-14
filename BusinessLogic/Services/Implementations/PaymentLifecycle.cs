using Application.Entities;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 5 — قواعد مشترک چرخهٔ عمر پرداخت که PaymentService، روال برگرداندن سفارش (OrderReversalService) و جاب
/// سفارش‌های رهاشده (AbandonedOrderCleanupService) باید یکسان اجرا کنند. ایستا و فقط وابسته به IUnitOfWork و
/// IAuditService است تا OrderService به PaymentService وابسته نشود (چرخهٔ DI).
///
/// «تأیید آغازشده» (Batch 41، FIN-11): پیش از فراخوانی Verify درگاه، <see cref="Payment.VerifyStartedAtUtc"/> روی
/// خودِ پرداخت نوشته می‌شود (در همان تراکنشِ «ادعا»). از آن لحظه تا وقتی پرداخت Pending است، ممکن است درگاه پول را
/// برداشته باشد (مثلاً پاسخ Verify در timeout گم شده)؛ چنین پرداختی هرگز بی‌صدا لغو نمی‌شود، وگرنه پولِ برداشته‌شده
/// بی‌ردپا می‌ماند. رکورد <see cref="VerificationStartedAction"/> در AuditLog همچنان نوشته می‌شود، اما فقط مدرک است؛
/// هیچ تصمیمی دیگر از جدول حسابرسی خوانده نمی‌شود.
/// </summary>
public static class PaymentLifecycle
{
    public const string VerificationStartedAction = "Payment.VerificationStarted";
    public const string VerificationPendingAction = "Payment.VerificationPending";
    public const string CancelledAction = "Payment.Cancelled";
    public const string NotVerifiedAction = "Payment.NotVerified";
    public const string ReviewRequiredAction = "Payment.ReviewRequired";
    public const string RefundRequestedAction = "Payment.RefundRequested";
    public const string RefundedAction = "Payment.Refunded";

    /// <summary>پرداختی که هنوز ممکن است پول بگیرد.</summary>
    public static bool IsOpen(Payment payment)
        => payment.State is PaymentState.Initiated or PaymentState.Pending;

    /// <summary>پرداخت Pending که Verify آن آغاز شده و نتیجه‌اش هنوز ثبت نشده است.</summary>
    public static bool IsVerificationUnresolved(Payment payment)
        => payment.State == PaymentState.Pending && payment.VerifyStartedAtUtc is not null;

    /// <summary>
    /// شناسهٔ پرداخت‌های Pending که Verify آن‌ها آغاز شده و نتیجه‌اش هنوز ثبت نشده است.
    /// Batch 41 (FIN-11): از ستون <see cref="Payment.VerifyStartedAtUtc"/>؛ بدون هیچ پرس‌وجویی از AuditLog.
    /// </summary>
    public static HashSet<int> FindUnresolvedVerifications(IEnumerable<Payment> payments)
        => payments.Where(IsVerificationUnresolved).Select(p => p.PaymentId).ToHashSet();

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

    /// <summary>
    /// Batch 41 (FIN-01): پرداخت موفق را به «در انتظار برگشت وجه» می‌برد و در AuditLog ثبت می‌کند. پول جابه‌جا
    /// نمی‌شود؛ این فقط کارِ بازِ اپراتور را می‌سازد. پرداخت باید ردیابی شده باشد؛ ذخیره با فراخوان است.
    /// </summary>
    public static async Task RequestRefundAsync(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        Payment payment,
        DateTime nowUtc,
        string reason,
        string source,
        CancellationToken cancellationToken)
    {
        payment.RequestRefund(nowUtc, reason);
        unitOfWork.Repository<Payment>().Update(payment);

        await auditService.RecordAsync(
            RefundRequestedAction,
            nameof(Payment),
            payment.PaymentId.ToString(),
            $"source={source} | order={payment.OrderId?.ToString() ?? "none"} | amount={payment.Amount} | reason={payment.RefundReason} " +
            "| MONEY_NOT_RETURNED_YET=true",
            cancellationToken);
    }
}
