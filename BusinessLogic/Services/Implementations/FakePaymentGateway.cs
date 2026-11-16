using Application.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — درگاه جعلی و قطعی برای تست و حالت Manual.
///
/// رفتار قابل پیش‌بینی است تا تست‌ها به شبکه وابسته نباشند:
///   • Initiate همیشه موفق است و Authority با پیشوند FAKE- می‌سازد.
///   • Verify برای Authority حاوی "fail" رد قطعی (Rejected)، برای Authority حاوی "timeout" نتیجهٔ
///     نامعلوم (TransientError)، برای تأیید تکراری همان Authority «قبلاً تأیید شده» (AlreadyVerified)
///     و در غیر این صورت Verified برمی‌گرداند.
///   • Inquire (Batch 43) هرگز «پرداخت‌شده» اختراع نمی‌کند: Authority که همین نمونه Verify کرده Verified،
///     Authority حاوی "timeout" نامعلوم (Unknown) و بقیه NotPaid هستند. پس جاب پرداخت‌های رهاشده در
///     Development همان رفتار قبلی (لغو) را دارد و هیچ سفارشی بدون Verify واقعی «پرداخت‌شده» نمی‌شود.
///   • Refund همیشه موفق است.
///
/// این «طلب» بودن عمدی است: هیچ تستی نباید برای سنجش منطق پول به اینترنت وصل شود.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly HashSet<string> _verified = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Task.FromResult(
                PaymentInitiationResult.Fail("INVALID_AMOUNT", "مبلغ پرداخت باید بزرگ‌تر از صفر باشد."));

        var authority = $"FAKE-{Guid.NewGuid():N}";

        return Task.FromResult(PaymentInitiationResult.Ok(authority, BuildRedirectUrl(authority)));
    }

    public Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return Task.FromResult(
                PaymentVerificationResult.Rejected("AUTHORITY_REQUIRED", "شناسهٔ پرداخت ارسال نشده است."));

        // کنوانسیون تست: هر Authority حاوی fail یعنی پرداخت ناموفق.
        if (authority.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(
                PaymentVerificationResult.Rejected("GATEWAY_FAILED", "پرداخت توسط درگاه تأیید نشد."));

        if (authority.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(
                PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "پاسخ درگاه در زمان مقرر نرسید."));

        lock (_sync)
        {
            var reference = $"REF-{authority}";
            return Task.FromResult(_verified.Add(authority)
                ? PaymentVerificationResult.Verified(reference)
                : PaymentVerificationResult.AlreadyVerified(reference));
        }
    }

    public Task<PaymentInquiryResult> InquireAsync(
        string authority,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return Task.FromResult(PaymentInquiryResult.NotPaid("NO_AUTHORITY"));

        if (authority.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(
                PaymentInquiryResult.Unknown("GATEWAY_TIMEOUT", "پاسخ درگاه در زمان مقرر نرسید."));

        lock (_sync)
        {
            return Task.FromResult(_verified.Contains(authority)
                ? PaymentInquiryResult.Verified("FAKE_VERIFIED")
                : PaymentInquiryResult.NotPaid("FAKE_NOT_PAID"));
        }
    }

    public string BuildRedirectUrl(string authority) => $"https://fake-gateway.local/pay/{authority}";

    public Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
