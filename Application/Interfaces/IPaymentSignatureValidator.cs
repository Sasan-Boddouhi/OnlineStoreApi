namespace Application.Interfaces;

/// <summary>
/// Phase 8 — اعتبارسنجی امضای webhook.
///
/// webhook با [AllowAnonymous] منتشر می‌شود، پس امضا تنها عامل احراز هویت است.
/// بدون این لایه، هر کسی می‌تواند «پرداخت موفق» جعلی ثبت کند.
/// </summary>
public interface IPaymentSignatureValidator
{
    /// <summary>ساخت امضا برای payload (مثلاً برای تست یا ارسال سمت درگاه).</summary>
    string Sign(string payload);

    /// <summary>مقایسهٔ امضای دریافتی با امضای محاسبه‌شده؛ مقایسه در زمان ثابت.</summary>
    bool Verify(string payload, string? signature);
}