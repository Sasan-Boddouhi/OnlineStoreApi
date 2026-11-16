using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

/// <summary>Batch 53 (API-04/06): یک ورودی از رجیستری کد خطا؛ کد ماشینی، وضعیت HTTP و پیام فارسی پیش‌فرض.</summary>
public sealed record ErrorCodeInfo(string Code, int Status, string Message);

/// <summary>
/// Batch 53 (API-04/06): رجیستری یگانهٔ کدهای خطای لایهٔ pipeline (احراز هویت، مسیریابی، پرس‌وجو، خطای داخلی).
/// هر کد فقط یک وضعیت HTTP دارد؛ سرویس‌ها باید از ثابت‌های این کلاس استفاده کنند، نه رشتهٔ خام.
/// </summary>
public static class ErrorCodes
{
    public static readonly ErrorCodeInfo Unauthenticated = new("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "برای دسترسی باید وارد حساب کاربری شوید.");
    public static readonly ErrorCodeInfo TokenExpired = new("TOKEN_EXPIRED", StatusCodes.Status401Unauthorized, "اعتبار نشست شما به پایان رسیده است. لطفاً توکن را تازه‌سازی کنید.");
    public static readonly ErrorCodeInfo TokenInvalid = new("TOKEN_INVALID", StatusCodes.Status401Unauthorized, "توکن ارسال‌شده معتبر نیست.");
    public static readonly ErrorCodeInfo SessionRevoked = new("SESSION_REVOKED", StatusCodes.Status401Unauthorized, "نشست شما لغو شده است. لطفاً دوباره وارد شوید.");
    public static readonly ErrorCodeInfo Forbidden = new("FORBIDDEN", StatusCodes.Status403Forbidden, "شما اجازهٔ انجام این عملیات را ندارید.");
    public static readonly ErrorCodeInfo NotFound = new("NOT_FOUND", StatusCodes.Status404NotFound, "مورد درخواستی یافت نشد.");
    public static readonly ErrorCodeInfo RouteNotFound = new("ROUTE_NOT_FOUND", StatusCodes.Status404NotFound, "مسیر درخواستی وجود ندارد.");
    public static readonly ErrorCodeInfo MethodNotAllowed = new("METHOD_NOT_ALLOWED", StatusCodes.Status405MethodNotAllowed, "این متد HTTP برای مسیر درخواستی مجاز نیست.");
    public static readonly ErrorCodeInfo UnsupportedMediaType = new("UNSUPPORTED_MEDIA_TYPE", StatusCodes.Status415UnsupportedMediaType, "نوع محتوای درخواست پشتیبانی نمی‌شود.");
    public static readonly ErrorCodeInfo InvalidQuery = new("INVALID_QUERY", StatusCodes.Status400BadRequest, "پارامترهای فیلتر یا مرتب‌سازی معتبر نیستند.");
    public static readonly ErrorCodeInfo ValidationError = new("VALIDATION_ERROR", StatusCodes.Status422UnprocessableEntity, "یک یا چند ورودی معتبر نیست.");
    public static readonly ErrorCodeInfo ConcurrencyConflict = new("CONCURRENCY_CONFLICT", StatusCodes.Status409Conflict, "تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.");
    public static readonly ErrorCodeInfo DuplicateRecord = new("DUPLICATE_RECORD", StatusCodes.Status409Conflict, "رکوردی با همین مشخصات قبلاً ثبت شده است.");
    public static readonly ErrorCodeInfo RateLimitExceeded = new("RATE_LIMIT_EXCEEDED", StatusCodes.Status429TooManyRequests, "تعداد درخواست‌های شما بیش از حد مجاز است.");
    public static readonly ErrorCodeInfo InternalError = new("INTERNAL_ERROR", StatusCodes.Status500InternalServerError, "خطای غیرمنتظره‌ای رخ داد. لطفاً بعداً دوباره تلاش کنید.");
    public static readonly ErrorCodeInfo PayoutOnHold = new("PAYOUT_ON_HOLD", StatusCodes.Status409Conflict, "این تسویه هنوز در دورهٔ نگه‌داشت است و قابل پرداخت نیست.");
    public static readonly ErrorCodeInfo PayoutNetChanged = new("PAYOUT_NET_CHANGED", StatusCodes.Status409Conflict, "مبلغ خالص تسویه تغییر کرده است؛ فهرست را تازه کنید.");
    public static readonly ErrorCodeInfo UserIsSystem = new("USER_IS_SYSTEM", StatusCodes.Status409Conflict, "این حساب سیستمی است و قابل تغییر نیست.");
    public static readonly ErrorCodeInfo PlatformWarehouseExists = new("PLATFORM_WAREHOUSE_EXISTS", StatusCodes.Status409Conflict, "انبار پلتفرم از قبل وجود دارد.");

    // Batch 46 (N9): تأیید شماره با OTP.
    public static readonly ErrorCodeInfo OtpInvalid = new("OTP_INVALID", StatusCodes.Status422UnprocessableEntity, "کد تأیید نادرست است یا دیگر معتبر نیست.");
    public static readonly ErrorCodeInfo OtpExpired = new("OTP_EXPIRED", StatusCodes.Status422UnprocessableEntity, "کد تأیید منقضی شده است. کد تازه درخواست کنید.");
    public static readonly ErrorCodeInfo OtpTooManyAttempts = new("OTP_TOO_MANY_ATTEMPTS", StatusCodes.Status429TooManyRequests, "تعداد تلاش‌های نادرست بیش از حد مجاز است. کد تازه درخواست کنید.");
    public static readonly ErrorCodeInfo PhoneNotVerified = new("PHONE_NOT_VERIFIED", StatusCodes.Status422UnprocessableEntity, "شماره موبایل تأیید نشده است. ابتدا کد تأیید را وارد کنید.");
    public static readonly ErrorCodeInfo OtpSendUnavailable = new("OTP_SEND_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable, "ارسال پیامک تأیید در حال حاضر ممکن نیست. لطفاً بعداً تلاش کنید.");

    // Batch 45c: مقدار هر قلم تابع واحد کالاست (Application.Common.Helpers.OrderQuantity).
    public static readonly ErrorCodeInfo QuantityInvalidForUnit = new("QUANTITY_INVALID_FOR_UNIT", StatusCodes.Status422UnprocessableEntity, "مقدار برای واحد این کالا مجاز نیست: کالای عددی فقط عدد صحیح ۱ یا بیشتر، کالای وزنی و حجمی حداکثر دو رقم اعشار.");

    /// <summary>همهٔ ورودی‌های ثبت‌شده؛ منبع تولید <c>openapi/error-codes.json</c>.</summary>
    public static IReadOnlyList<ErrorCodeInfo> All { get; } = new[]
    {
        Unauthenticated, TokenExpired, TokenInvalid, SessionRevoked, Forbidden, NotFound, RouteNotFound,
        MethodNotAllowed, UnsupportedMediaType, InvalidQuery, ValidationError, ConcurrencyConflict,
        DuplicateRecord, RateLimitExceeded, InternalError,
        PayoutOnHold, PayoutNetChanged, UserIsSystem, PlatformWarehouseExists,
        OtpInvalid, OtpExpired, OtpTooManyAttempts, PhoneNotVerified, OtpSendUnavailable,
        QuantityInvalidForUnit
    };

    /// <summary>کد پیش‌فرض یک وضعیت HTTP برای پاسخ‌های بدون کد صریح (MVC)؛ فقط وضعیت‌هایی که دقیقاً یک ورودی عمومی دارند.</summary>
    public static ErrorCodeInfo? ForStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status500InternalServerError => InternalError,
        _ => null
    };

    public static ErrorCodeInfo? Find(string code) =>
        All.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.Ordinal));

    /// <summary>نشانی <c>type</c> در ProblemDetails؛ URN پایدار به‌جای میزبان جایگزین.</summary>
    public static string TypeUri(string code) => $"urn:onlinestore:error:{code.ToLowerInvariant().Replace('_', '-')}";
}
