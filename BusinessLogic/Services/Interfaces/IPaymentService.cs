using BusinessLogic.DTOs.Payment;

namespace BusinessLogic.Services.Interfaces;

/// <summary>Phase 8 — سرویس پرداخت.</summary>
public interface IPaymentService
{
    /// <summary>
    /// ثبت دستی پرداخت روی یک فاکتور (فاز ۰ — حفظ‌شده).
    /// برای تسویه‌های خارج از درگاه استفاده می‌شود.
    /// </summary>
    Task<PaymentDto> RecordPaymentAsync(
        int invoiceId,
        decimal amount,
        string transactionId,
        CancellationToken cancellationToken = default);

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
    /// Phase 9 (R43) — ثبت برگشت وجه توسط ادمین.
    ///
    /// پول را جابه‌جا نمی‌کند (زرین‌پال برگشت خودکار ندارد)؛ آنچه انجام می‌دهد
    /// این است که سیستم را با واقعیت هم‌راست کند: پرداخت Refunded، سفارش
    /// Returned/Cancelled و تسویه‌های باز فروشنده لغو.
    /// </summary>
    Task<PaymentDto> RefundPaymentAsync(
        int paymentId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}