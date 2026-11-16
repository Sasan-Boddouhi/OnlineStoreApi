using Application.Entities;
using BusinessLogic.DTOs.Auth;

namespace BusinessLogic.Services.Interfaces;

/// <summary>Batch 46 (N9، review §9.3): کد یک‌بار مصرف پیامکی و توکن تأیید شماره.</summary>
public interface IOtpService
{
    /// <summary>
    /// کد تازه می‌سازد و پیامک می‌کند. پاسخ برای هر شمارهٔ معتبر یکسان است (حساب داشته باشد یا نه).
    /// سقف هر شماره → 429 RATE_LIMIT_EXCEEDED (با Retry-After)؛ بودجهٔ روزانه یا خطای ارائه‌دهنده → 503 OTP_SEND_UNAVAILABLE.
    /// </summary>
    Task<OtpSendResultDto> SendAsync(OtpSendDto dto, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// کد را با زمان ثابت می‌سنجد. نادرست یا ناموجود → 422 OTP_INVALID؛ منقضی → 422 OTP_EXPIRED؛
    /// پس از سقف تلاش → 429 OTP_TOO_MANY_ATTEMPTS. موفق → توکن تأیید یک‌بار مصرف.
    /// </summary>
    Task<OtpVerifyResultDto> VerifyAsync(OtpVerifyDto dto, CancellationToken ct = default);

    /// <summary>
    /// توکن تأیید را برای همین شماره (استاندارد) و هدف مصرف می‌کند. توکن نبود، نادرست، منقضی یا مصرف‌شده → 422 PHONE_NOT_VERIFIED.
    /// تغییر در همان UnitOfWork ذخیره می‌شود تا با تراکنش فراخواننده برگردانده شود.
    /// </summary>
    Task ConsumeVerificationAsync(string canonicalPhone, OtpPurpose purpose, string? verificationToken, CancellationToken ct = default);
}
