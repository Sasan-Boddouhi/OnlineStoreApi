using System.Diagnostics.CodeAnalysis;
using Application.Exceptions;

namespace Application.Common.Helpers;

/// <summary>
/// Batch 48b: تنها نرمال‌ساز شمارهٔ موبایل ایران در کل API (جایگزین قاعدهٔ سخت Batch 48 به تصمیم مالک).
/// <para>
/// ورودی‌های پذیرفته: ارقام فارسی (۰–۹) و عربی (٠–٩)، فاصله، خط تیره، پرانتز و نقطه بین ارقام،
/// و پیش‌شماره‌های <c>+98…</c>، <c>0098…</c>، <c>98…</c> (۱۲ رقم)، <c>9xxxxxxxxx</c> (۱۰ رقم) و <c>09xxxxxxxxx</c>.
/// خروجی همیشه شکل استاندارد <c>09xxxxxxxxx</c> (دقیقاً ۱۱ رقم ASCII) است که به‌تنهایی ذخیره و جست‌وجو می‌شود.
/// </para>
/// <para>
/// ثبت‌نام، ورود، ایجاد/ویرایش کاربر توسط ادمین، شمارهٔ تحویل‌گیرندهٔ سفارش و هر جریان بعدی که شماره می‌گیرد
/// (OTP، بازیابی رمز) باید پیش از هر جست‌وجو یا ذخیره همین کلاس را صدا بزنند.
/// </para>
/// </summary>
public static class IranianPhoneNumber
{
    /// <summary>پیام کاربر برای شماره‌ای که قابل نرمال‌سازی نیست؛ اعتبارسنج‌ها هم همین متن را برمی‌گردانند.</summary>
    public const string ErrorMessage = "شماره موبایل معتبر نیست (مثال: 09123456789)";

    /// <summary>کد خطای 422 وقتی سرویس مستقیم (بدون اعتبارسنج) با شمارهٔ نامعتبر صدا زده شود.</summary>
    public const string InvalidErrorCode = "INVALID_PHONE_NUMBER";

    private const int CanonicalLength = 11;

    /// <summary>
    /// شماره را به شکل استاندارد <c>09xxxxxxxxx</c> می‌برد. اگر ورودی موبایل ایران نباشد
    /// (تلفن ثابت، طول اشتباه، حرف، پیش‌شمارهٔ کشور دیگر، دو علامت +، خالی) <c>false</c> برمی‌گرداند.
    /// </summary>
    public static bool TryNormalize(string? input, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        Span<char> digits = stackalloc char[Math.Min(trimmed.Length, 32)];
        var count = 0;
        var hasPlus = false;

        foreach (var ch in trimmed)
        {
            if (IsSeparator(ch))
                continue;

            if (ch == '+')
            {
                // فقط یک + و فقط پیش از نخستین رقم.
                if (hasPlus || count > 0)
                    return false;
                hasPlus = true;
                continue;
            }

            var ascii = ToAsciiDigit(ch);
            if (ascii is null || count == digits.Length)
                return false;

            digits[count++] = ascii.Value;
        }

        ReadOnlySpan<char> number = digits[..count];

        if (hasPlus)
        {
            // +98 9xxxxxxxxx
            if (number.Length != 12 || !number.StartsWith("98"))
                return false;
            number = number[2..];
        }
        else if (number.Length == 14 && number.StartsWith("0098"))
        {
            number = number[4..];
        }
        else if (number.Length == 12 && number.StartsWith("98"))
        {
            number = number[2..];
        }

        // اکنون یا ۱۰ رقمِ 9xxxxxxxxx است یا ۱۱ رقمِ 09xxxxxxxxx.
        string result;
        if (number.Length == CanonicalLength - 1 && number[0] == '9')
            result = string.Concat("0", number);
        else
            result = new string(number);

        if (result.Length != CanonicalLength || result[0] != '0' || result[1] != '9')
            return false;

        canonical = result;
        return true;
    }

    /// <summary>شکل استاندارد، یا <c>null</c> اگر ورودی قابل نرمال‌سازی نباشد.</summary>
    public static string? NormalizeOrNull(string? input)
        => TryNormalize(input, out var canonical) ? canonical : null;

    /// <summary>
    /// شکل استاندارد؛ اگر ورودی قابل نرمال‌سازی نباشد 422 <see cref="InvalidErrorCode"/>.
    /// برای مرز سرویس که پیش از هر جست‌وجو یا ذخیره فراخوانی می‌شود (در مسیر HTTP اعتبارسنج پیش‌تر رد کرده است).
    /// </summary>
    public static string NormalizeOrThrow(string? input)
        => TryNormalize(input, out var canonical)
            ? canonical
            : throw new UnprocessableEntityException(ErrorMessage, InvalidErrorCode);

    /// <summary>آیا ورودی به یک موبایل ایران قابل نرمال‌سازی است.</summary>
    public static bool IsValid([NotNullWhen(true)] string? input) => TryNormalize(input, out _);

    private static bool IsSeparator(char ch)
        => ch is ' ' or '-' or '(' or ')' or '.'
            or ' '                 // فاصلهٔ نشکن
            or '‌' or '‍'     // نیم‌فاصله و اتصال
            or '‎' or '‏'     // نشانه‌های جهت متن (کپی از متن راست‌به‌چپ)
            or '‐' or '‑' or '‒' or '–' or '—' or '−' // انواع خط تیره
            || char.IsWhiteSpace(ch);

    private static char? ToAsciiDigit(char ch) => ch switch
    {
        >= '0' and <= '9' => ch,
        >= '۰' and <= '۹' => (char)('0' + (ch - '۰')), // ارقام فارسی
        >= '٠' and <= '٩' => (char)('0' + (ch - '٠')), // ارقام عربی
        _ => null
    };
}
