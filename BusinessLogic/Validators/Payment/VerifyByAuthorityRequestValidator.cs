using BusinessLogic.DTOs.Payment;
using FluentValidation;

namespace BusinessLogic.Validators.Payment;

/// <summary>Batch 43 (FIN-05): تطبیق دستی بدون Authority پذیرفته نمی‌شود.</summary>
public class VerifyByAuthorityRequestValidator : AbstractValidator<VerifyByAuthorityRequest>
{
    public VerifyByAuthorityRequestValidator()
    {
        RuleFor(x => x.Authority)
            .Must(a => !string.IsNullOrWhiteSpace(a)).WithMessage("شناسهٔ پرداخت (Authority) الزامی است.")
            .MaximumLength(100).WithMessage("شناسهٔ پرداخت نباید بیش از ۱۰۰ کاراکتر باشد.");
    }
}
