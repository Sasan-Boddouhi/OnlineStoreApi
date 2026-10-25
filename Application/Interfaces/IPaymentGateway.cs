namespace Application.Interfaces;

/// <summary>
/// Phase 8 — قرارداد درگاه پرداخت.
///
/// این قرارداد عمداً هیچ اشاره‌ای به اطلاعات کارت ندارد:
/// PAN/CVV هرگز از سمت ما خوانده یا ذخیره نمی‌شود؛ کاربر در صفحهٔ درگاه
/// پرداخت می‌کند و ما فقط Authority و نتیجه را می‌گیریم.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>شروع پرداخت؛ خروجی شامل Authority و آدرس هدایت کاربر است.</summary>
    Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>تأیید پرداخت پس از بازگشت کاربر یا از طریق webhook.</summary>
    Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>برگشت وجه. اگر درگاه پشتیبانی نکند false برمی‌گردد.</summary>
    Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default);
}

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

public sealed record PaymentVerificationResult(
    bool Success,
    string? ReferenceId,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PaymentVerificationResult Ok(string? referenceId)
        => new(true, referenceId, null, null);

    public static PaymentVerificationResult Fail(string code, string message)
        => new(false, null, code, message);
}