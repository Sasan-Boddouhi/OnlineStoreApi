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
    /// Phase 9 (R43) / Batch 41 — ثبت برگشت وجه توسط ادمین («پول را دستی برگردانده‌ام»).
    ///
    /// پول را جابه‌جا نمی‌کند (زرین‌پال برگشت خودکار ندارد). پرداخت RefundPending را Refunded می‌کند؛ پرداخت
    /// Succeeded را ابتدا از روال مشترک برگرداندن سفارش (سفارش Returned/Cancelled، بسته‌ها، موجودی، کوپن،
    /// تسویه‌ها و بدهی‌ها) به RefundPending و در همان تراکنش به Refunded می‌برد. وضعیت دیگر ⇒ 409 PAYMENT_NOT_REFUNDABLE.
    /// </summary>
    Task<PaymentDto> RefundPaymentAsync(
        int paymentId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
