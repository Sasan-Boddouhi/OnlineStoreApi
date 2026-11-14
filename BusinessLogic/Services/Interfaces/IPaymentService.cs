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
}
