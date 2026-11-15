using Asp.Versioning;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Online_Store_Application.Extensions;

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
    /// Batch 5: درخواست تکراری برای سفارشی که پرداخت باز دارد همان پرداخت را برمی‌گرداند (isReplay=true)؛
    /// 409 ORDER_ALREADY_PAID / ORDER_NOT_PAYABLE / PAYMENT_VERIFICATION_PENDING.
    /// </summary>
    [HttpPost("initiate")]
    [Authorize]
    [EnableRateLimiting("PaymentInitiateLimiter")]
    [ProducesResponseType(typeof(InitiatePaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InitiatePaymentResultDto>> Initiate(
        [FromBody] InitiatePaymentRequest body,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? body.IdempotencyKey;

        var result = await _paymentService.InitiateAsync(body.OrderId, key, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// تأیید پرداخت پس از بازگشت کاربر از درگاه. Batch 5: سفارش لغوشده یا پرداخت‌شده → 409 ORDER_NOT_PAYABLE /
    /// ORDER_ALREADY_PAID و درگاه صدا زده نمی‌شود؛ نتیجهٔ نامعلوم درگاه → 503 PAYMENT_VERIFY_RETRY با هدر Retry-After
    /// (پرداخت Pending می‌ماند و تکرار همین درخواست آن را قطعی می‌کند).
    /// Batch 43: هر تأیید یک تماس با درگاه است؛ سقف نرخ برای هر کاربر (RateLimiting:PaymentVerify، پیش‌فرض ۱۰ در
    /// دقیقه) ⇒ 429 RATE_LIMIT_EXCEEDED با هدر Retry-After.
    /// </summary>
    [HttpPost("verify")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.PaymentVerify)]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PaymentDto>> Verify(
        [FromQuery] string authority,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.VerifyAsync(authority, cancellationToken));

    /// <summary>
    /// Batch 43 (FIN-05) — تطبیق دستی یک پرداخت با درگاه توسط ادمین («پرداخت کردم ولی سفارشم ثبت نشد»).
    ///
    /// از درگاه استعلام می‌کند و همان قواعد جاب پرداخت‌های رهاشده را برای همین یک Authority اجرا می‌کند:
    ///   • پرداخت‌شدهٔ تأییدنشده و سفارش هنوز قابل پرداخت ⇒ برداشت از مسیر Verify (<c>outcome = Captured</c>)؛
    ///   • قبلاً نزد درگاه تأییدشده ⇒ Succeeded ثبت می‌شود؛ اگر سفارش دیگر قابل پرداخت نیست، با برگشت وجه لازم به
    ///     صف بررسی می‌رود (<c>CapturedNeedsReview</c>)؛
    ///   • پرداخت‌شدهٔ تأییدنشده برای سفارش ناپرداختنی ⇒ بدون برداشت لغو می‌شود و درگاه پول را برمی‌گرداند (<c>Cancelled</c>)؛
    ///   • پرداخت‌نشده یا پاسخ نامعلوم درگاه ⇒ دست نمی‌خورد (<c>LeftPending</c>)؛ پرداخت بسته ⇒ <c>NoChange</c>.
    /// پاسخ همیشه 200 با شرح نتیجه است و هر فراخوانی در AuditLog (<c>Payment.VerifyByAuthority</c>) ثبت می‌شود.
    /// 404 PAYMENT_NOT_FOUND برای Authority ناموجود؛ 422 بدون Authority.
    /// </summary>
    [HttpPost("verify-by-authority")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PaymentReconciliationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentReconciliationDto>> VerifyByAuthority(
        [FromBody] VerifyByAuthorityRequest body,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.VerifyByAuthorityAsync(body.Authority ?? string.Empty, cancellationToken));

    /// <summary>
    /// Webhook درگاه.
    ///
    /// این endpoint [AllowAnonymous] است چون درگاه JWT نمی‌فرستد.
    /// احراز هویت آن **فقط** امضای دیجیتال هدر `X-Payment-Signature` است،
    /// و امضا قبل از هر تفسیری از بدنه بررسی می‌شود.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

    /// <summary>دریافت پرداخت بر اساس شناسه سفارش.</summary>
    [HttpGet("order/{orderId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaymentDto>> GetByOrder(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentService.GetByOrderAsync(orderId, cancellationToken);
        return payment is null ? NoContent() : Ok(payment);
    }

    /// <summary>
    /// Phase 9 (R43) / Batch 42 — <b>درخواست</b> برگشت وجه توسط ادمین (دلیل الزامی).
    ///
    /// <b>این endpoint پول را برنمی‌گرداند.</b> سفارش برگردانده می‌شود (بسته‌ها، موجودی، کوپن، تسویه‌ها) و پرداخت
    /// به <c>RefundPending</c> می‌رود و در صف برگشت وجه (<c>GET payments/refunds</c>) می‌ماند. پس از واریز در پنل
    /// بانکی، <c>POST payments/{id}/refund/confirm</c> با شمارهٔ پیگیری آن را <c>Refunded</c> می‌کند.
    /// 409 PAYMENT_NOT_REFUNDABLE برای پرداختی که موفق نیست (از جمله درخواست‌شده).
    /// </summary>
    [HttpPost("{paymentId:int}/refund")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentDto>> Refund(
        int paymentId,
        [FromBody] RefundPaymentRequest body,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.RefundPaymentAsync(paymentId, body.Reason, cancellationToken));

    /// <summary>
    /// Batch 42 (FIN-12) — تأیید برگشت واقعی پول (پس از واریز در بانک): RefundPending ⇒ Refunded با شمارهٔ پیگیری
    /// بانکی و تاریخ اختیاری. 409 PAYMENT_NOT_REFUND_PENDING برای وضعیت دیگر؛ 422 بدون شمارهٔ پیگیری.
    /// </summary>
    [HttpPost("{paymentId:int}/refund/confirm")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentDto>> ConfirmRefund(
        int paymentId,
        [FromBody] ConfirmRefundRequest body,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.ConfirmRefundAsync(paymentId, body, cancellationToken));

    /// <summary>
    /// Batch 42 (FIN-12) — صف برگشت وجه: پرداخت‌های RefundPending، قدیمی‌ترین درخواست اول.
    /// با <c>includeRefunded=true</c> برگشت‌های تأییدشده هم (پس از آن‌ها، تازه‌ترین اول) می‌آیند.
    /// </summary>
    [HttpGet("refunds")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(BusinessLogic.DTOs.Shared.PagedResult<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BusinessLogic.DTOs.Shared.PagedResult<PaymentDto>>> GetRefundQueue(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeRefunded = false,
        CancellationToken cancellationToken = default)
        => Ok(await _paymentService.GetRefundQueueAsync(pageNumber, pageSize, includeRefunded, cancellationToken));

    /// <summary>
    /// Batch 42 (FIN-12, CEO-02) — پرداخت‌هایی که بررسی دستی می‌خواهند (برداشت پول برای سفارش ناپرداختنی، تأییدِ
    /// بی‌نتیجه، ناسازگاری پس از برداشت)، قدیمی‌ترین اول. با <c>includeResolved=true</c> بسته‌شده‌ها هم می‌آیند.
    /// </summary>
    [HttpGet("review")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(BusinessLogic.DTOs.Shared.PagedResult<PaymentReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BusinessLogic.DTOs.Shared.PagedResult<PaymentReviewDto>>> GetReviewQueue(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeResolved = false,
        CancellationToken cancellationToken = default)
        => Ok(await _paymentService.GetReviewQueueAsync(pageNumber, pageSize, includeResolved, cancellationToken));

    /// <summary>
    /// Batch 42 (FIN-12) — بستن بررسی دستی یک پرداخت با شرح آنچه انجام شد.
    /// 409 PAYMENT_REVIEW_NOT_OPEN اگر بررسیِ بازی ندارد؛ 422 بدون شرح.
    /// </summary>
    [HttpPost("{paymentId:int}/review/resolve")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PaymentReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentReviewDto>> ResolveReview(
        int paymentId,
        [FromBody] ResolvePaymentReviewRequest body,
        CancellationToken cancellationToken)
        => Ok(await _paymentService.ResolveReviewAsync(paymentId, body.Note ?? string.Empty, cancellationToken));
}
