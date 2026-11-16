namespace Application.Options;

/// <summary>
/// Batch 46 (review §9.3): تنظیمات کد یک‌بار مصرف (<c>Otp:*</c>). پیش‌فرض‌ها همان قواعد بازبینی‌اند:
/// کد ۶ رقمی با عمر ۲ دقیقه، ۵ تلاش، هر شماره ۱ ارسال در ۱۲۰ ثانیه و ۵ در ساعت و ۱۰ در روز، و سقف روزانهٔ کل پیامک‌ها.
/// </summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>کمینهٔ طول کلید HMAC (کاراکتر).</summary>
    public const int MinHmacKeyLength = 32;

    /// <summary>کلید HMAC-SHA-256 برای هش کد و توکن تأیید. در Production الزامی (حداقل ۳۲ کاراکتر)؛ بیرون از آن اگر خالی باشد کلید تصادفی همان فرایند ساخته می‌شود.</summary>
    public string HmacKey { get; set; } = string.Empty;

    public int CodeLifetimeSeconds { get; set; } = 120;

    /// <summary>سقف تلاش تأیید برای هر کد؛ پس از آن کد مرده است (OTP_TOO_MANY_ATTEMPTS).</summary>
    public int MaxVerifyAttempts { get; set; } = 5;

    /// <summary>فاصلهٔ کمینه بین دو ارسال به یک شماره (همهٔ هدف‌ها).</summary>
    public int ResendCooldownSeconds { get; set; } = 120;

    public int MaxSendsPerPhonePerHour { get; set; } = 5;

    public int MaxSendsPerPhonePerDay { get; set; } = 10;

    /// <summary>بودجهٔ روزانهٔ کل پیامک‌های OTP (روز UTC). با پر شدن، ارسال 503 OTP_SEND_UNAVAILABLE می‌شود و خطا لاگ می‌شود (هشدار).</summary>
    public int GlobalDailySendLimit { get; set; } = 5000;

    /// <summary>عمر توکن تأیید شماره (فرصت پر کردن فرم ثبت‌نام پس از تأیید کد).</summary>
    public int VerificationTokenLifetimeSeconds { get; set; } = 900;

    /// <summary>خطاهای پیکربندی؛ فهرست خالی یعنی معتبر.</summary>
    public IReadOnlyList<string> Validate(string? environmentName, bool smsSenderIsReal)
    {
        var errors = new List<string>();
        var production = ProductionConfigurationRules.IsProduction(environmentName);

        if (!string.IsNullOrEmpty(HmacKey) && HmacKey.Length < MinHmacKeyLength)
            errors.Add($"Otp:HmacKey باید حداقل {MinHmacKeyLength} کاراکتر باشد.");
        if (production && string.IsNullOrWhiteSpace(HmacKey))
            errors.Add($"Otp:HmacKey باید در Production تنظیم شود (حداقل {MinHmacKeyLength} کاراکتر تصادفی).");

        if (CodeLifetimeSeconds is < 30 or > 600)
            errors.Add("Otp:CodeLifetimeSeconds باید بین 30 و 600 باشد.");
        if (MaxVerifyAttempts is < 1 or > 10)
            errors.Add("Otp:MaxVerifyAttempts باید بین 1 و 10 باشد.");
        if (ResendCooldownSeconds is < 0 or > 3600)
            errors.Add("Otp:ResendCooldownSeconds باید بین 0 و 3600 باشد.");
        if (production && ResendCooldownSeconds < 60)
            errors.Add("Otp:ResendCooldownSeconds باید در Production حداقل 60 باشد.");
        if (MaxSendsPerPhonePerHour < 1)
            errors.Add("Otp:MaxSendsPerPhonePerHour باید حداقل 1 باشد.");
        if (MaxSendsPerPhonePerDay < MaxSendsPerPhonePerHour)
            errors.Add("Otp:MaxSendsPerPhonePerDay نباید از Otp:MaxSendsPerPhonePerHour کمتر باشد.");
        if (GlobalDailySendLimit < 1)
            errors.Add("Otp:GlobalDailySendLimit باید حداقل 1 باشد.");
        if (VerificationTokenLifetimeSeconds is < 60 or > 3600)
            errors.Add("Otp:VerificationTokenLifetimeSeconds باید بین 60 و 3600 باشد.");

        // N9: در Production فرستندهٔ لاگ‌کننده پذیرفته نیست؛ آداپتور واقعی (Batch 46b) لازم است.
        if (production && !smsSenderIsReal)
            errors.Add("Sms:Provider در Production باید یک ارائه‌دهندهٔ واقعی پیامک باشد؛ فرستندهٔ لاگ‌کننده فقط بیرون از Production مجاز است.");

        return errors;
    }
}

/// <summary>Batch 46: انتخاب فرستندهٔ پیامک (<c>Sms:*</c>). فعلاً فقط <c>Logging</c>؛ آداپتور واقعی در Batch 46b.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public const string LoggingProvider = "Logging";

    /// <summary>نام ارائه‌دهنده. خالی یعنی <see cref="LoggingProvider"/>.</summary>
    public string Provider { get; set; } = LoggingProvider;
}
