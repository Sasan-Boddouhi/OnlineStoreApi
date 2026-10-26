using Asp.Versioning;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 8 — پرداخت.
///
/// نکتهٔ امنیتی: هیچ endpointای اطلاعات کارت نمی‌گیرد.
/// شمارهٔ کارت و CVV فقط در صفحهٔ درگاه تایپ می‌شود، نه در API ما.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>
    /// شروع پرداخت برای یک سفارش.
    ///
    /// IdempotencyKey از هدر `Idempotency-Key` یا بدنه خوانده می‌شود.
    /// اگر کلاینت به‌خاطر قطعی شبکه دوباره درخواست بدهد، همان پرداخت قبلی برمی‌گردد
    /// و پول دوباره از حساب کاربر کم نمی‌شود.
    /// </summary>
    [HttpPost("initiate")]
    [Authorize]
    public async Task<ActionResult<InitiatePaymentResultDto>> Initiate(
        [FromBody] InitiatePaymentRequest body,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? body.IdempotencyKey;

        var result = await _paymentService.InitiateAsync(body.OrderId, key, cancellationToken);

        return Ok(result);
    }

    /// <summary>تأیید پرداخت پس از بازگشت کاربر از درگاه.</summary>
    [HttpPost("verify")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> Verify(
        [FromQuery] string authority,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.VerifyAsync(authority, cancellationToken));

    /// <summary>
    /// Webhook درگاه.
    ///
    /// این endpoint [AllowAnonymous] است چون درگاه JWT نمی‌فرستد.
    /// احراز هویت آن **فقط** امضای دیجیتال هدر `X-Payment-Signature` است،
    /// و امضا قبل از هر تفسیری از بدنه بررسی می‌شود.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        // بدنهٔ خام لازم است؛ بازسازی JSON امضا را می‌شکند.
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var signature = Request.Headers["X-Payment-Signature"].FirstOrDefault();

        await _paymentService.HandleWebhookAsync(payload, signature, cancellationToken);

        // پاسخ 200 حتی برای webhook تکراری: درگاه نباید دوباره تلاش کند.
        return Ok(new { received = true });
    }

    [HttpGet("order/{orderId:int}")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> GetByOrder(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentService.GetByOrderAsync(orderId, cancellationToken);
        return payment is null ? NoContent() : Ok(payment);
    }

    /// <summary>
    /// Phase 9 (R43) — ثبت برگشت وجه توسط ادمین.
    ///
    /// <b>این endpoint پول را برنمی‌گرداند.</b> زرین‌پال برگشت خودکار ندارد؛
    /// ادمین باید مبلغ را در پنل بانکی برگرداند و بعد این را صدا بزند تا
    /// سیستم با واقعیت هم‌راست شود. بدون این، گزارش مالی می‌گفت پول نزد ماست.
    ///
    /// اثرها: Payment → Refunded، Order → Returned/Cancelled،
    /// تسویه‌های باز فروشنده لغو، و ثبت در AuditLog با پرچم
    /// <c>MONEY_MOVED_MANUALLY=true</c>.
    /// </summary>
    [HttpPost("{paymentId:int}/refund")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<PaymentDto>> Refund(
        int paymentId,
        [FromBody] RefundPaymentRequest? body,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.RefundPaymentAsync(paymentId, body?.Reason, cancellationToken));
}