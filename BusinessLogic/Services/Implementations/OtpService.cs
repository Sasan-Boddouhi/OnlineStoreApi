using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Common.Helpers;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Validators.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 46 (F-12، N9، review §9.3): کد ۶ رقمی از مولد رمزنگاشتی، فقط HMAC-SHA-256 آن ذخیره می‌شود، عمر کوتاه، یک‌بار مصرف،
/// یک کد فعال برای هر (شماره، هدف)، حداکثر <see cref="OtpOptions.MaxVerifyAttempts"/> تلاش با مقایسهٔ زمان ثابت، سقف ارسال برای هر
/// شماره و بودجهٔ روزانهٔ کل، و پاسخ یکسان برای شمارهٔ دارای حساب و بدون حساب. حساب سیستمی (IsSystem) هرگز کد نمی‌گیرد.
/// <para>
/// سقف هر IP در لایهٔ HTTP است (سیاست‌های OtpSend و OtpVerify در RateLimitingExtensions). کد و توکن هرگز لاگ نمی‌شوند.
/// </para>
/// </summary>
public sealed class OtpService : IOtpService
{
    private const int VerificationTokenBytes = 32;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly TimeProvider _timeProvider;
    private readonly OtpOptions _options;
    private readonly ILogger<OtpService> _logger;
    private readonly byte[] _key;

    public OtpService(
        IUnitOfWork unitOfWork,
        ISmsSender smsSender,
        TimeProvider timeProvider,
        IOptions<OtpOptions> options,
        ILogger<OtpService> logger)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_options.HmacKey))
            throw new InvalidOperationException("Otp:HmacKey is not configured.");
        _key = Encoding.UTF8.GetBytes(_options.HmacKey);
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private IGenericRepository<OtpCode> Codes => _unitOfWork.Repository<OtpCode>();

    // ================= SEND =================
    public async Task<OtpSendResultDto> SendAsync(OtpSendDto dto, string? clientIp, CancellationToken ct = default)
    {
        // directive 2 (Batch 48b): فقط شکل استاندارد؛ شمارهٔ غیرموبایل ایران هرگز پیامک نمی‌گیرد (422).
        var phone = IranianPhoneNumber.NormalizeOrThrow(dto.PhoneNumber);
        EnsureKnownPurpose(dto.Purpose);
        var now = UtcNow;

        await EnforcePerPhoneLimitsAsync(phone, now, ct);
        await EnforceGlobalBudgetAsync(now, ct);

        var result = new OtpSendResultDto
        {
            ExpiresInSeconds = _options.CodeLifetimeSeconds,
            ResendAfterSeconds = _options.ResendCooldownSeconds
        };

        // پاسخ مالک به Batch 49b: حساب سیستمی هرگز کد نمی‌گیرد. پاسخ همان پاسخ عمومی است.
        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.PhoneNumber == phone && u.IsSystem, ct))
        {
            _logger.LogWarning("OTP request for a system account refused (purpose {Purpose}).", dto.Purpose);
            return result;
        }

        // یک کد فعال برای هر (شماره، هدف): کدهای قبلی دیگر قابل تأیید نیستند.
        var previous = await Codes.ListAsync(
            new Spec<OtpCode>()
                .Where(o => o.PhoneNumber == phone && o.Purpose == dto.Purpose && o.ConsumedAtUtc == null)
                .AsTracking(),
            ct);
        foreach (var old in previous)
        {
            old.ConsumedAtUtc = now;
            old.Version++;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var entity = new OtpCode
        {
            PhoneNumber = phone,
            Purpose = dto.Purpose,
            CodeHash = HashCode(phone, dto.Purpose, code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(_options.CodeLifetimeSeconds),
            RequestIp = Truncate(clientIp, 45)
        };

        await Codes.AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _smsSender.SendAsync(new SmsMessage(phone, BuildText(code)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // کد ذخیره شده ولی تحویل نشده؛ دیگر قابل تأیید نیست و در سقف‌ها شمرده می‌شود (ضد تکرار پرهزینه).
            entity.ConsumedAtUtc = now;
            entity.Version++;
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogError(ex, "OTP SMS delivery failed for {Phone}.", IranianPhoneMask(phone));
            throw new ServiceUnavailableException(ErrorCodes.OtpSendUnavailable.Message, ErrorCodes.OtpSendUnavailable.Code, 30);
        }

        _logger.LogInformation("OTP sent to {Phone} (purpose {Purpose}).", IranianPhoneMask(phone), dto.Purpose);
        return result;
    }

    private async Task EnforcePerPhoneLimitsAsync(string phone, DateTime now, CancellationToken ct)
    {
        var dayAgo = now.AddDays(-1);
        var recent = await Codes.ListAsync(
            new Spec<OtpCode>().Where(o => o.PhoneNumber == phone && o.CreatedAtUtc > dayAgo),
            ct);

        if (recent.Count == 0)
            return;

        var latest = recent.Max(o => o.CreatedAtUtc);
        var cooldownEnds = latest.AddSeconds(_options.ResendCooldownSeconds);
        if (cooldownEnds > now)
            throw TooMany(cooldownEnds - now);

        var hourAgo = now.AddHours(-1);
        var lastHour = recent.Where(o => o.CreatedAtUtc > hourAgo).Select(o => o.CreatedAtUtc).OrderBy(t => t).ToList();
        if (lastHour.Count >= _options.MaxSendsPerPhonePerHour)
            throw TooMany(lastHour[lastHour.Count - _options.MaxSendsPerPhonePerHour].AddHours(1) - now);

        if (recent.Count >= _options.MaxSendsPerPhonePerDay)
        {
            var lastDay = recent.Select(o => o.CreatedAtUtc).OrderBy(t => t).ToList();
            throw TooMany(lastDay[lastDay.Count - _options.MaxSendsPerPhonePerDay].AddDays(1) - now);
        }
    }

    private async Task EnforceGlobalBudgetAsync(DateTime now, CancellationToken ct)
    {
        var dayStart = now.Date;
        var sentToday = await Codes.CountAsync(o => o.CreatedAtUtc >= dayStart, ct);
        if (sentToday < _options.GlobalDailySendLimit)
            return;

        // review §9.3 قاعدهٔ ۳: بودجهٔ روزانه با هشدار. سطح Error تا هشدار عملیاتی بگیرد.
        _logger.LogError(
            "OTP global daily SMS budget reached ({Sent}/{Limit}); OTP sending is paused until 00:00 UTC.",
            sentToday, _options.GlobalDailySendLimit);

        var retryAfter = (int)Math.Ceiling((dayStart.AddDays(1) - now).TotalSeconds);
        throw new ServiceUnavailableException(ErrorCodes.OtpSendUnavailable.Message, ErrorCodes.OtpSendUnavailable.Code, Math.Max(1, retryAfter));
    }

    // ================= VERIFY =================
    public async Task<OtpVerifyResultDto> VerifyAsync(OtpVerifyDto dto, CancellationToken ct = default)
    {
        var phone = IranianPhoneNumber.NormalizeOrNull(dto.PhoneNumber);
        if (phone is null || !OtpCodeText.TryNormalize(dto.Code, out var code) || !Enum.IsDefined(dto.Purpose))
            throw new UnprocessableEntityException(ErrorCodes.OtpInvalid.Message, ErrorCodes.OtpInvalid.Code);

        var now = UtcNow;

        // آخرین کد این (شماره، هدف). حساب سیستمی هرگز کد ندارد؛ پس پاسخ آن همان «کد نادرست» است.
        var otp = await Codes.FirstOrDefaultAsync(
            new Spec<OtpCode>()
                .Where(o => o.PhoneNumber == phone && o.Purpose == dto.Purpose)
                .OrderByDescending(o => o.CreatedAtUtc)
                .ThenByDescending(o => o.OtpCodeId)
                .AsTracking(),
            ct);

        if (otp is null)
            throw Invalid();

        if (otp.AttemptCount >= _options.MaxVerifyAttempts)
            throw TooManyAttempts(otp, now);

        if (otp.ConsumedAtUtc is not null)
            throw Invalid();

        if (otp.ExpiresAtUtc <= now)
            throw new UnprocessableEntityException(ErrorCodes.OtpExpired.Message, ErrorCodes.OtpExpired.Code);

        otp.AttemptCount++;
        otp.Version++;

        // مقایسهٔ زمان ثابت روی HMAC؛ کد ورودی هیچ‌گاه با مقدار ذخیره‌شده رشته‌ای مقایسه نمی‌شود.
        var matches = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(HashCode(phone, dto.Purpose, code)),
            Convert.FromHexString(otp.CodeHash));

        if (!matches)
        {
            if (otp.AttemptCount >= _options.MaxVerifyAttempts)
                otp.ConsumedAtUtc = now;   // کد مرده است؛ تلاش بعدی OTP_TOO_MANY_ATTEMPTS می‌گیرد.

            await SaveAttemptAsync(ct);
            _logger.LogWarning("Wrong OTP for {Phone} (attempt {Attempt}/{Max}).",
                IranianPhoneMask(phone), otp.AttemptCount, _options.MaxVerifyAttempts);
            throw Invalid();
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(VerificationTokenBytes));
        otp.ConsumedAtUtc = now;
        otp.VerifiedAtUtc = now;
        otp.VerificationTokenHash = HashToken(token);
        otp.VerificationExpiresAtUtc = now.AddSeconds(_options.VerificationTokenLifetimeSeconds);

        await SaveAttemptAsync(ct);

        _logger.LogInformation("OTP verified for {Phone} (purpose {Purpose}).", IranianPhoneMask(phone), dto.Purpose);
        return new OtpVerifyResultDto
        {
            VerificationToken = token,
            ExpiresInSeconds = _options.VerificationTokenLifetimeSeconds
        };
    }

    /// <summary>دو تأیید هم‌زمان یک تلاش را دو بار مصرف نمی‌کنند: بازنده (تعارض Version) «کد نادرست» می‌گیرد.</summary>
    private async Task SaveAttemptAsync(CancellationToken ct)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Invalid();
        }
    }

    // ================= CONSUME =================
    public async Task ConsumeVerificationAsync(string canonicalPhone, OtpPurpose purpose, string? verificationToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(verificationToken) || verificationToken.Length > 128)
            throw NotVerified();

        var now = UtcNow;
        var tokenHash = HashToken(verificationToken.Trim());

        var otp = await Codes.FirstOrDefaultAsync(
            new Spec<OtpCode>()
                .Where(o => o.PhoneNumber == canonicalPhone && o.Purpose == purpose && o.VerificationTokenHash == tokenHash)
                .AsTracking(),
            ct);

        if (otp is null || otp.VerificationUsedAtUtc is not null
            || otp.VerificationExpiresAtUtc is null || otp.VerificationExpiresAtUtc <= now)
            throw NotVerified();

        otp.VerificationUsedAtUtc = now;
        otp.Version++;

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw NotVerified();   // همان توکن هم‌زمان در درخواست دیگری مصرف شد.
        }
    }

    // ================= helpers =================
    private string HashCode(string phone, OtpPurpose purpose, string code) =>
        Hmac($"otp|{(int)purpose}|{phone}|{code}");

    private string HashToken(string token) => Hmac($"verification|{token}");

    private string Hmac(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)));

    private static string BuildText(string code) =>
        $"کد تأیید شما: {code}\nاین کد را به هیچ‌کس ندهید.";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>شماره در لاگ فقط به شکل 0912***6789.</summary>
    public static string IranianPhoneMask(string phone) =>
        phone.Length == 11 ? $"{phone[..4]}***{phone[7..]}" : "***";

    private static void EnsureKnownPurpose(OtpPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new UnprocessableEntityException("هدف کد تأیید معتبر نیست.", ErrorCodes.ValidationError.Code);
    }

    private static TooManyRequestsException TooMany(TimeSpan retryAfter) =>
        new(ErrorCodes.RateLimitExceeded, (int)Math.Ceiling(retryAfter.TotalSeconds),
            "برای این شماره به‌تازگی کد فرستاده شده است. لطفاً کمی بعد دوباره تلاش کنید.");

    private TooManyRequestsException TooManyAttempts(OtpCode otp, DateTime now)
    {
        // کد تازه پس از پایان فاصلهٔ ارسال دوباره ممکن است.
        var resendAt = otp.CreatedAtUtc.AddSeconds(_options.ResendCooldownSeconds);
        return new TooManyRequestsException(ErrorCodes.OtpTooManyAttempts, (int)Math.Ceiling((resendAt - now).TotalSeconds));
    }

    private static UnprocessableEntityException Invalid() =>
        new(ErrorCodes.OtpInvalid.Message, ErrorCodes.OtpInvalid.Code);

    private static UnprocessableEntityException NotVerified() =>
        new(ErrorCodes.PhoneNotVerified.Message, ErrorCodes.PhoneNotVerified.Code);
}
