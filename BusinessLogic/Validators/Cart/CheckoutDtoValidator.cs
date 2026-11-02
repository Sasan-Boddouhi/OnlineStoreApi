using BusinessLogic.DTOs.Cart;
using FluentValidation;

namespace BusinessLogic.Validators.Cart;

public class CheckoutDtoValidator : AbstractValidator<CheckoutDto>
{
    public CheckoutDtoValidator()
    {
        RuleFor(x => x.ShippingFullName)
            .NotEmpty().WithMessage("نام تحویل‌گیرنده الزامی است.")
            .MaximumLength(100).WithMessage("نام تحویل‌گیرنده نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.ShippingAddress)
            .NotEmpty().WithMessage("آدرس تحویل الزامی است.")
            .MaximumLength(500).WithMessage("آدرس تحویل نباید بیش از ۵۰۰ کاراکتر باشد.");

        RuleFor(x => x.ShippingPhoneNumber)
            .NotEmpty().WithMessage("شماره تماس الزامی است.")
            .Matches(@"^(\+98|0)?9\d{9}$").WithMessage("فرمت شماره همراه معتبر نیست.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("توضیحات نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}
