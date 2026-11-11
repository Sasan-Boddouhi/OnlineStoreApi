using System.ComponentModel.DataAnnotations;

namespace Application.Options;

/// <summary>
/// Batch 11 (F-13) — سیاست طول عمر نشست کاربر.
///
/// access token کوتاه‌عمر است (<see cref="JwtOptions.ExpireMinutes"/>) و نشست با refresh زنده می‌ماند:
///  • بی‌کاری: اگر از آخرین ورود یا refresh موفق بیش از <see cref="IdleTimeoutDays"/> روز بگذرد، نشست منقضی می‌شود.
///    طول عمر هر refresh token هم همین مقدار است.
///  • سقف مطلق: <see cref="AbsoluteLifetimeDays"/> روز پس از ورود، کاربر باید دوباره وارد شود، حتی اگر فعال بوده باشد.
///  • refresh token چرخیده‌ای که پس از <see cref="RefreshReuseGraceSeconds"/> ثانیه دوباره بیاید نشانهٔ سرقت است
///    و کل نشست را باطل می‌کند؛ در همین بازهٔ کوتاه فقط رد می‌شود تا رقابت دو تب هم‌زمان کاربر را بیرون نیندازد.
///
/// فعالیت عمداً در هر درخواست ثبت نمی‌شود (نوشتن در دیتابیس برای هر درخواست)؛ refresh همان سیگنال فعالیت است.
/// قیدهای بین این مقدارها و JwtOptions در UserSessionOptionsValidator هنگام راه‌اندازی بررسی می‌شوند.
/// </summary>
public sealed class UserSessionOptions
{
    public const string SectionName = "Session";

    [Range(1, 365, ErrorMessage = "Session:IdleTimeoutDays must be between 1 and 365.")]
    public int IdleTimeoutDays { get; set; } = 7;

    [Range(1, 3650, ErrorMessage = "Session:AbsoluteLifetimeDays must be between 1 and 3650.")]
    public int AbsoluteLifetimeDays { get; set; } = 30;

    [Range(0, 300, ErrorMessage = "Session:RefreshReuseGraceSeconds must be between 0 and 300.")]
    public int RefreshReuseGraceSeconds { get; set; } = 10;

    public TimeSpan IdleTimeout => TimeSpan.FromDays(IdleTimeoutDays);

    public TimeSpan AbsoluteLifetime => TimeSpan.FromDays(AbsoluteLifetimeDays);

    public TimeSpan RefreshReuseGracePeriod => TimeSpan.FromSeconds(RefreshReuseGraceSeconds);
}
