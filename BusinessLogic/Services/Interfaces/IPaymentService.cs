using BusinessLogic.DTOs.Payment;

namespace BusinessLogic.Services.Interfaces;

/// <summary>Phase 8 — سرویس پرداخت.</summary>
public interface IPaymentService
{
    /// <summary>
    /// شروع پرداخت برای سفارش.
    /// اگر idempotencyKey تکراری باشد، همان پرداخت قبلی برگردانده می‌شود.
    /// </summary>
    Task<InitiatePaymentResultDto> InitiateAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>تأیید پرداخت پس از بازگشت کاربر از درگاه.</summary>
    Task<PaymentDto> VerifyAsync(string authority, CancellationToken cancellationToken = default);

    /// <summary>پردازش webhook درگاه؛ امضا اولین و تنها عامل احراز هویت است.</summary>
    Task<PaymentDto> HandleWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default);

    Task<PaymentDto?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 9 (R43) / Batch 41 / Batch 42 — <b>درخواست</b> برگشت وجه توسط ادمین.
    ///
    /// پول را جابه‌جا نمی‌کند. پرداخت Succeeded را از روال مشترک برگرداندن سفارش (سفارش Returned/Cancelled، بسته‌ها،
    /// موجودی، کوپن، تسویه‌ها و بدهی‌ها) به RefundPending می‌برد و در صف برگشت وجه می‌گذارد. برگشت واقعی پول با
    /// <see cref="ConfirmRefundAsync"/> ثبت می‌شود. وضعیت دیگر (از جمله RefundPending) ⇒ 409 PAYMENT_NOT_REFUNDABLE.
    /// </summary>
    Task<PaymentDto> RefundPaymentAsync(
        int paymentId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 42 (FIN-12) — ادمین تأیید می‌کند که پول در بانک به مشتری برگشته است: RefundPending ⇒ Refunded با شمارهٔ
    /// پیگیری بانکی. پرداختِ غیر RefundPending ⇒ 409 PAYMENT_NOT_REFUND_PENDING.
    /// </summary>
    Task<PaymentDto> ConfirmRefundAsync(
        int paymentId,
        ConfirmRefundRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 42 (FIN-12) — صف برگشت وجه: پرداخت‌های RefundPending (قدیمی‌ترین درخواست اول) و سپس، اگر
    /// <paramref name="includeRefunded"/> باشد، Refundedها (تازه‌ترین اول).
    /// </summary>
    Task<BusinessLogic.DTOs.Shared.PagedResult<PaymentDto>> GetRefundQueueAsync(
        int pageNumber,
        int pageSize,
        bool includeRefunded = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 42 (FIN-12, CEO-02) — پرداخت‌هایی که بررسی دستی می‌خواهند (قدیمی‌ترین اول). با
    /// <paramref name="includeResolved"/> بررسی‌های بسته‌شده هم (پس از بازها) می‌آیند.
    /// </summary>
    Task<BusinessLogic.DTOs.Shared.PagedResult<PaymentReviewDto>> GetReviewQueueAsync(
        int pageNumber,
        int pageSize,
        bool includeResolved = false,
        CancellationToken cancellationToken = default);

    /// <summary>Batch 42 (FIN-12) — بستن بررسی دستی یک پرداخت. بررسیِ باز نداشتن ⇒ 409 PAYMENT_REVIEW_NOT_OPEN.</summary>
    Task<PaymentReviewDto> ResolveReviewAsync(
        int paymentId,
        string note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 43 (FIN-05) — اقدام ادمین: تطبیق یک Authority با درگاه. از درگاه استعلام می‌کند و همان قواعد جاب
    /// پرداخت‌های رهاشده را اجرا می‌کند، با دو تفاوت: پرداختِ «پرداخت‌نشده» را لغو نمی‌کند (شاید خریدار هنوز در صفحهٔ
    /// بانک است؛ لغو با جاب است) و برای پاسخ نامعلوم پرداخت را به صف بررسی نمی‌فرستد (ادمین خودش در حال بررسی است).
    /// پرداخت‌شدهٔ تأییدنشده برای سفارش قابل پرداخت ⇒ برداشت از مسیر Verify؛ Authority ناموجود ⇒ 404 PAYMENT_NOT_FOUND.
    /// </summary>
    Task<PaymentReconciliationDto> VerifyByAuthorityAsync(
        string authority,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 43 (FIN-05) — تطبیق یک پرداخت Pending کهنه توسط جاب پرداخت‌های رهاشده، پیش از لغو:
    ///   • پرداخت‌شده و سفارش قابل پرداخت ⇒ برداشت از مسیر Verify (source = reconciliation)؛
    ///   • قبلاً نزد درگاه تأییدشده ⇒ ثبت Succeeded از مسیر Verify (و بررسی/برگشت وجه اگر سفارش قابل پرداخت نیست)؛
    ///   • پرداخت‌شدهٔ تأییدنشده برای سفارش ناپرداختنی ⇒ بدون Verify لغو و به صف بررسی می‌رود (درگاه پول را برمی‌گرداند)؛
    ///   • پرداخت‌نشده ⇒ لغو (مثل قبل)؛ نامعلوم ⇒ Pending می‌ماند و یک بار به صف بررسی می‌رود؛
    ///   • پرداختی که Verify آن قبلاً آغاز شده هرگز لغو نمی‌شود.
    /// با <paramref name="askGateway"/> = false رفتار پیش از Batch 43 اجرا می‌شود (بدون استعلام).
    /// </summary>
    /// <param name="paymentId">شناسهٔ پرداخت.</param>
    /// <param name="askGateway">پیش از تصمیم از درگاه استعلام شود.</param>
    /// <param name="abandonedAfterMinutes">مهلت رهاشدگی؛ فقط برای متن AuditLog.</param>
    /// <param name="cancellationToken">فقط برای بارگذاری پرداخت؛ تصمیم پول پس از آغاز لغوپذیر نیست.</param>
    Task<PaymentReconciliationDto> ReconcileStalePaymentAsync(
        int paymentId,
        bool askGateway,
        int abandonedAfterMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 43 (FIN-05) — جاب سفارش‌های رهاشده پیش از لغو یک سفارش، برای هر پرداخت بازِ آن از درگاه استعلام می‌کند
    /// (سفارش ۶۰ دقیقه‌ای ممکن است پرداختی جوان‌تر از مهلت جاب پرداخت‌ها داشته باشد). همان قواعد
    /// <see cref="ReconcileStalePaymentAsync"/>، با این تفاوت که پرداختِ «پرداخت‌نشده» را خودش لغو نمی‌کند: لغو آن با
    /// روال برگرداندن سفارش است (مثل قبل). فراخوان از <c>GatewayStatus</c> و <c>Outcome</c> تصمیم می‌گیرد:
    /// برداشت‌شده ⇒ سفارش دیگر رهاشده نیست؛ نامعلوم ⇒ سفارش در این اجرا لغو نمی‌شود.
    /// </summary>
    Task<PaymentReconciliationDto> ReconcileBeforeOrderCancellationAsync(
        int paymentId,
        CancellationToken cancellationToken = default);
}
