using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities;

/// <summary>
/// Batch 46 (F-12، review §9.3): یک کد یک‌بار مصرف که با پیامک فرستاده شده است.
/// <para>
/// خود کد هرگز ذخیره نمی‌شود؛ فقط HMAC-SHA-256 آن با کلید سرور (<c>Otp:HmacKey</c>) روی «هدف|شماره|کد».
/// پس از تأیید موفق یک «توکن تأیید شماره» صادر می‌شود که فقط HMAC آن ذخیره می‌شود و یک بار (مثلاً برای ثبت‌نام) مصرف می‌شود.
/// </para>
/// </summary>
[Table("OtpCode")]
public class OtpCode
{
    public const int HashLength = 64;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long OtpCodeId { get; set; }

    /// <summary>شکل استاندارد <c>09xxxxxxxxx</c> (IranianPhoneNumber).</summary>
    [Required]
    [StringLength(11)]
    public string PhoneNumber { get; set; } = string.Empty;

    public OtpPurpose Purpose { get; set; }

    /// <summary>HMAC-SHA-256 (hex) کد؛ مقایسه فقط با زمان ثابت.</summary>
    [Required]
    [StringLength(HashLength)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>تعداد تلاش‌های تأیید (درست یا غلط). با رسیدن به سقف، کد مرده است.</summary>
    public int AttemptCount { get; set; }

    /// <summary>کد دیگر قابل تأیید نیست: تأیید شد، کد تازه‌تری جای آن را گرفت، سقف تلاش پر شد یا ارسال پیامک شکست خورد.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>HMAC (hex) توکن تأیید شماره که پس از تأیید موفق به کلاینت داده شد.</summary>
    [StringLength(HashLength)]
    public string? VerificationTokenHash { get; set; }

    public DateTime? VerificationExpiresAtUtc { get; set; }

    /// <summary>توکن تأیید مصرف شد (مثلاً ثبت‌نام کامل شد)؛ استفادهٔ دوباره رد می‌شود.</summary>
    public DateTime? VerificationUsedAtUtc { get; set; }

    /// <summary>IP درخواست‌کننده (برای بررسی سوءاستفاده)؛ همان مقدار حل‌شدهٔ ForwardedHeaders.</summary>
    [StringLength(45)]
    public string? RequestIp { get; set; }

    /// <summary>توکن هم‌زمانی: دو تأیید موازی روی یک کد نمی‌توانند هر دو یک «تلاش» را مصرف کنند.</summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}

/// <summary>Batch 46: هدف کد. Batch 47 «بازیابی رمز» را اضافه می‌کند.</summary>
public enum OtpPurpose
{
    /// <summary>ثبت‌نام (N9): ثبت‌نام فقط با توکن تأیید همین هدف کامل می‌شود.</summary>
    Register = 1
}
