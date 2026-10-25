using Application.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — درگاه جعلی و قطعی برای تست و حالت Manual.
///
/// رفتار قابل پیش‌بینی است تا تست‌ها به شبکه وابسته نباشند:
///   • Initiate همیشه موفق است و Authority با پیشوند FAKE- می‌سازد.
///   • Verify شکست می‌خورد اگر Authority شامل "fail" باشد (حالت خطا).
///   • Refund همیشه موفق است.
///
/// این «طلب» بودن عمدی است: هیچ تستی نباید برای سنجش منطق پول به اینترنت وصل شود.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Task.FromResult(
                PaymentInitiationResult.Fail("INVALID_AMOUNT", "مبلغ پرداخت باید بزرگ‌تر از صفر باشد."));

        var authority = $"FAKE-{Guid.NewGuid():N}";
        var redirect = $"https://fake-gateway.local/pay/{authority}";

        return Task.FromResult(PaymentInitiationResult.Ok(authority, redirect));
    }

    public Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return Task.FromResult(
                PaymentVerificationResult.Fail("AUTHORITY_REQUIRED", "شناسهٔ پرداخت ارسال نشده است."));

        // کنوانسیون تست: هر Authority حاوی fail یعنی پرداخت ناموفق.
        if (authority.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(
                PaymentVerificationResult.Fail("GATEWAY_FAILED", "پرداخت توسط درگاه تأیید نشد."));

        return Task.FromResult(PaymentVerificationResult.Ok($"REF-{authority}"));
    }

    public Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}