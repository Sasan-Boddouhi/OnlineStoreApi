using BusinessLogic.DTOs.Auth;
using BusinessLogic.Validators.Common;
using FluentValidation;

namespace BusinessLogic.Validators.Auth;

/// <summary>Batch 46: شماره با نرمال‌ساز مشترک (Batch 48b) سنجیده می‌شود؛ شمارهٔ غیرموبایل ایران هرگز پیامک نمی‌گیرد.</summary>
public class OtpSendDtoValidator : AbstractValidator<OtpSendDto>
{
    public OtpSendDtoValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("شماره تماس الزامی است")
            .MustBeIranianMobile();

        RuleFor(x => x.Purpose)
            .IsInEnum().WithMessage("هدف کد تأیید معتبر نیست");
    }
}

/// <summary>Batch 46: کد دقیقاً ۶ رقم (ارقام فارسی و عربی هم پذیرفته می‌شوند).</summary>
public class OtpVerifyDtoValidator : AbstractValidator<OtpVerifyDto>
{
    public OtpVerifyDtoValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("شماره تماس الزامی است")
            .MustBeIranianMobile();

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("کد تأیید الزامی است")
            .Must(code => OtpCodeText.TryNormalize(code, out _)).WithMessage("کد تأیید باید ۶ رقم باشد");

        RuleFor(x => x.Purpose)
            .IsInEnum().WithMessage("هدف کد تأیید معتبر نیست");
    }
}

/// <summary>Batch 46: ارقام فارسی/عربی کد را به ASCII می‌برد؛ فقط ۶ رقم پذیرفته است.</summary>
public static class OtpCodeText
{
    public const int Length = 6;

    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (trimmed.Length != Length)
            return false;

        Span<char> digits = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            var ch = trimmed[i];
            char? ascii = ch switch
            {
                >= '0' and <= '9' => ch,
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => null
            };
            if (ascii is null)
                return false;
            digits[i] = ascii.Value;
        }

        code = new string(digits);
        return true;
    }
}
