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

    public static PaymentVerificationResult Verified(string? referenceId)
        => new(PaymentVerificationOutcome.Verified, referenceId, null, null);

    public static PaymentVerificationResult AlreadyVerified(string? referenceId)
        => new(PaymentVerificationOutcome.AlreadyVerified, referenceId, null, null);

    public static PaymentVerificationResult Rejected(string code, string message)
        => new(PaymentVerificationOutcome.Rejected, null, code, message);

    public static PaymentVerificationResult TransientError(string code, string message)
        => new(PaymentVerificationOutcome.TransientError, null, code, message);
}
