using System.Text.RegularExpressions;
using FluentValidation;

namespace BusinessLogic.Validators.Common;

/// <summary>
/// Batch 48 (BE-08): تنها قالب پذیرفتهٔ شمارهٔ موبایل ایران در کل API، <c>09xxxxxxxxx</c>.
/// ورود، ثبت‌نام و تسویه‌حساب همه از این قاعده استفاده می‌کنند تا شمارهٔ ذخیره‌شده با شمارهٔ ورود یکی بماند.
/// </summary>
public static class PhoneNumber
{
    public const string ErrorMessage = "شماره موبایل معتبر نیست (مثال: 09123456789)";

    private static readonly Regex Pattern = new("^09[0-9]{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? value) => value is not null && Pattern.IsMatch(value);

    public static IRuleBuilderOptions<T, string?> MustBeIranianMobile<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(IsValid).WithMessage(ErrorMessage);
}
