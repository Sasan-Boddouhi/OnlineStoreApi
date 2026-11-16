using Application.Entities;

namespace BusinessLogic.DTOs.Auth;

/// <summary>Batch 46: درخواست ارسال کد تأیید. شماره با همان نرمال‌ساز مشترک (Batch 48b) پذیرفته می‌شود.</summary>
public class OtpSendDto
{
    public string PhoneNumber { get; set; } = null!;

    /// <summary>هدف کد؛ پیش‌فرض ثبت‌نام (1).</summary>
    public OtpPurpose Purpose { get; set; } = OtpPurpose.Register;
}

/// <summary>
/// Batch 46: پاسخ یکسان ارسال کد — برای شمارهٔ دارای حساب و بدون حساب دقیقاً همین بدنه و همین وضعیت (ضد شمارش حساب).
/// </summary>
public class OtpSendResultDto
{
    /// <summary>عمر کد (ثانیه).</summary>
    public int ExpiresInSeconds { get; set; }

    /// <summary>کمترین فاصله تا ارسال دوباره به همین شماره (ثانیه).</summary>
    public int ResendAfterSeconds { get; set; }
}

/// <summary>Batch 46: تأیید کد دریافتی.</summary>
public class OtpVerifyDto
{
    public string PhoneNumber { get; set; } = null!;

    /// <summary>کد ۶ رقمی (ارقام فارسی و عربی هم پذیرفته می‌شوند).</summary>
    public string Code { get; set; } = null!;

    public OtpPurpose Purpose { get; set; } = OtpPurpose.Register;
}

/// <summary>Batch 46: نتیجهٔ تأیید موفق. توکن یک‌بار مصرف است و در <c>auth/register</c> به‌عنوان <c>phoneVerificationToken</c> فرستاده می‌شود.</summary>
public class OtpVerifyResultDto
{
    public string VerificationToken { get; set; } = null!;

    /// <summary>عمر توکن تأیید (ثانیه).</summary>
    public int ExpiresInSeconds { get; set; }
}
