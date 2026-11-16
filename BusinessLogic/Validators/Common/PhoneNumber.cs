using Application.Common.Helpers;
using FluentValidation;

namespace BusinessLogic.Validators.Common;

/// <summary>
/// Batch 48 (BE-08) و Batch 48b: تنها قاعدهٔ شمارهٔ موبایل ایران در کل API.
/// هر ورودی که <see cref="IranianPhoneNumber"/> بتواند به <c>09xxxxxxxxx</c> نرمال کند پذیرفته می‌شود
/// (<c>+98…</c>، <c>0098…</c>، ارقام فارسی، فاصله و خط تیره)؛ بقیه با همان پیام رد می‌شوند.
/// سرویس‌ها پیش از جست‌وجو یا ذخیره همان نرمال‌ساز را صدا می‌زنند، پس فقط شکل استاندارد ذخیره می‌شود.
/// ورود، ثبت‌نام، تسویه‌حساب و ایجاد/ویرایش کاربر توسط ادمین همه از این قاعده استفاده می‌کنند.
/// </summary>
public static class PhoneNumber
{
    public const string ErrorMessage = IranianPhoneNumber.ErrorMessage;

    public static bool IsValid(string? value) => IranianPhoneNumber.IsValid(value);

    public static IRuleBuilderOptions<T, string?> MustBeIranianMobile<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(IsValid).WithMessage(ErrorMessage);
}
