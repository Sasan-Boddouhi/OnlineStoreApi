namespace Application.Interfaces;

/// <summary>
/// Phase 8 — قرارداد درگاه پرداخت.
///
/// این قرارداد عمداً هیچ اشاره‌ای به اطلاعات کارت ندارد:
/// PAN/CVV هرگز از سمت ما خوانده یا ذخیره نمی‌شود؛ کاربر در صفحهٔ درگاه
/// پرداخت می‌کند و ما فقط Authority و نتیجه را می‌گیریم.
///
/// Batch 5: قرارداد مستقل از درگاه است. کدهای اختصاصی درگاه (مثلاً 100/101 زرین‌پال)، شکل
/// درخواست/پاسخ، واحد پول و آدرس صفحهٔ پرداخت فقط داخل پیاده‌سازی درگاه می‌مانند؛ PaymentService
/// فقط روی <see cref="PaymentVerificationOutcome"/> تصمیم می‌گیرد تا درگاه دوم (مثلاً زیبال) بدون
/// تغییر PaymentService اضافه شود.
///
/// مبالغ همیشه به <b>ریال</b> و عدد صحیح هستند (تصمیم مالک D2).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>شروع پرداخت؛ خروجی شامل Authority و آدرس هدایت کاربر است.</summary>
    Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// تأیید (برداشت نهایی) پرداخت پس از بازگشت کاربر. تکرار آن مجاز است: پرداختی که قبلاً تأیید شده
    /// <see cref="PaymentVerificationOutcome.AlreadyVerified"/> برمی‌گرداند. خطای شبکه، timeout و 5xx
    /// <see cref="PaymentVerificationOutcome.TransientError"/> است، نه رد پرداخت. این متد پرتاب نمی‌کند.
    /// </summary>
    Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch 43 (FIN-05): استعلام وضعیت یک Authority از درگاه، <b>بدون</b> برداشت پول. فقط برای تصمیم‌گیری است
    /// (آیا خریدار پرداخت کرده؟ آیا قبلاً تأیید شده؟)؛ برداشت نهایی همچنان فقط با <see cref="VerifyAsync"/> است.
    /// خطای شبکه، timeout، 5xx، پاسخ ناخوانا و وضعیت ناشناخته <see cref="PaymentInquiryStatus.Unknown"/> است.
    /// این متد پرتاب نمی‌کند.
    /// </summary>
    Task<PaymentInquiryResult> InquireAsync(
        string authority,
        CancellationToken cancellationToken = default);

    /// <summary>آدرس صفحهٔ پرداخت درگاه برای یک Authority موجود (برای replay شروع پرداخت).</summary>
    string BuildRedirectUrl(string authority);

    /// <summary>برگشت وجه. اگر درگاه پشتیبانی نکند false برمی‌گردد.</summary>
    Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default);
}

/// <summary>درخواست شروع پرداخت. Amount به ریال و عدد صحیح است.</summary>
public sealed record PaymentInitiationRequest(
    int OrderId,
    decimal Amount,
    string CallbackUrl,
    string? Description = null);

public sealed record PaymentInitiationResult(
    bool Success,
    string? Authority,
    string? RedirectUrl,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PaymentInitiationResult Ok(string authority, string redirectUrl)
        => new(true, authority, redirectUrl, null, null);

    public static PaymentInitiationResult Fail(string code, string message)
        => new(false, null, null, code, message);
}

/// <summary>Batch 5 (F-07): نتیجهٔ مستقل از درگاهِ تأیید پرداخت.</summary>
public enum PaymentVerificationOutcome
{
    /// <summary>همین الان تأیید و برداشت شد.</summary>
    Verified,

    /// <summary>قبلاً تأیید شده بود (تکرار تأیید)؛ پول برداشته شده است.</summary>
    AlreadyVerified,

    /// <summary>درگاه قطعاً رد کرد (پرداخت انجام نشده، مبلغ نادرست، Authority نامعتبر)؛ پولی برداشته نشده است.</summary>
    Rejected,

    /// <summary>
    /// نتیجه نامعلوم است (خطای شبکه، timeout، 5xx، پاسخ ناخوانا یا خطای پیکربندی). ممکن است پول برداشته
    /// شده باشد؛ پرداخت نباید Failed شود و تأیید بعداً تکرار می‌شود.
    /// </summary>
    TransientError
}

public sealed record PaymentVerificationResult(
    PaymentVerificationOutcome Outcome,
    string? ReferenceId,
    string? ErrorCode,
    string? ErrorMessage)
{
    /// <summary>پول برداشته شده است (تأیید الان یا قبلاً).</summary>
    public bool IsCaptured => Outcome is PaymentVerificationOutcome.Verified or PaymentVerificationOutcome.AlreadyVerified;

    /// <summary>Batch 43 (FIN-07): کارمزد درگاه برای این تراکنش، همان عددی که درگاه برگردانده است (اگر برگردانده باشد).</summary>
    public decimal? Fee { get; init; }

    /// <summary>Batch 43 (FIN-07): پرداخت‌کنندهٔ کارمزد به گفتهٔ درگاه (مثلاً Merchant یا Payer).</summary>
    public string? FeeType { get; init; }

    /// <summary>
    /// Batch 43 (FIN-07): شمارهٔ کارت <b>ماسک‌شده</b> به گفتهٔ درگاه (مثلاً 502229******5995). شمارهٔ کامل کارت هرگز
    /// خوانده یا نگه‌داری نمی‌شود؛ ذخیره فقط از راه <c>Payment.RecordGatewayDetails</c> است که دوباره ماسک می‌کند.
    /// </summary>
    public string? CardPanMasked { get; init; }

    public static PaymentVerificationResult Verified(string? referenceId)
        => new(PaymentVerificationOutcome.Verified, referenceId, null, null);

    public static PaymentVerificationResult AlreadyVerified(string? referenceId)
        => new(PaymentVerificationOutcome.AlreadyVerified, referenceId, null, null);

    public static PaymentVerificationResult Rejected(string code, string message)
        => new(PaymentVerificationOutcome.Rejected, null, code, message);

    public static PaymentVerificationResult TransientError(string code, string message)
        => new(PaymentVerificationOutcome.TransientError, null, code, message);
}

/// <summary>Batch 43 (FIN-05): وضعیت مستقل از درگاهِ یک Authority هنگام استعلام.</summary>
public enum PaymentInquiryStatus
{
    /// <summary>
    /// درگاه برای این Authority پولی نزد ما نگه نداشته است: پرداخت انجام نشده، ناموفق بوده، هنوز در صفحهٔ بانک
    /// مانده یا برگشت خورده است. چون Verify نمی‌شود، هر پرداخت دیرهنگام را خودِ درگاه به خریدار برمی‌گرداند.
    /// </summary>
    NotPaid,

    /// <summary>خریدار پرداخت کرده ولی هنوز Verify نشده است؛ پول تا Verify برداشت قطعی نشده است.</summary>
    PaidNotVerified,

    /// <summary>پرداخت قبلاً Verify شده است؛ پول برداشته شده است.</summary>
    Verified,

    /// <summary>درگاه پاسخ قطعی نداد (خطای شبکه، timeout، 5xx، محدودیت نرخ، وضعیت ناشناخته، پیکربندی).</summary>
    Unknown
}

/// <summary>
/// Batch 43 (FIN-05): نتیجهٔ استعلام. <see cref="GatewayStatus"/> متن خامِ وضعیت درگاه است (فقط برای لاگ و AuditLog؛
/// هیچ تصمیمی از روی آن گرفته نمی‌شود).
/// </summary>
public sealed record PaymentInquiryResult(
    PaymentInquiryStatus Status,
    string? GatewayStatus,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PaymentInquiryResult NotPaid(string? gatewayStatus = null)
        => new(PaymentInquiryStatus.NotPaid, gatewayStatus, null, null);

    public static PaymentInquiryResult PaidNotVerified(string? gatewayStatus = null)
        => new(PaymentInquiryStatus.PaidNotVerified, gatewayStatus, null, null);

    public static PaymentInquiryResult Verified(string? gatewayStatus = null)
        => new(PaymentInquiryStatus.Verified, gatewayStatus, null, null);

    public static PaymentInquiryResult Unknown(string code, string message, string? gatewayStatus = null)
        => new(PaymentInquiryStatus.Unknown, gatewayStatus, code, message);

    /// <summary>کد خطای «استعلام برای این درگاه خاموش است» (هیچ تماسی با درگاه گرفته نشده).</summary>
    public const string InquiryDisabledCode = "INQUIRY_DISABLED";

    /// <summary>
    /// استعلام با تنظیمات خاموش است (مثلاً <c>PaymentGatewaySettings:EnableInquiry = false</c>): وضعیت نامعلوم است و
    /// هیچ تماسی با درگاه گرفته نشده. فراخوان نباید آن را قطعیِ درگاه حساب کند.
    /// </summary>
    public static PaymentInquiryResult Disabled()
        => new(PaymentInquiryStatus.Unknown, null, InquiryDisabledCode, "استعلام وضعیت پرداخت از درگاه خاموش است.");

    /// <summary>استعلام خاموش بوده است (نه اینکه درگاه پاسخ نداده باشد).</summary>
    public bool IsInquiryDisabled => Status == PaymentInquiryStatus.Unknown && ErrorCode == InquiryDisabledCode;
}
