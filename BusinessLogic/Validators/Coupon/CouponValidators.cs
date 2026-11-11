using BusinessLogic.DTOs.Coupon;
using FluentValidation;

namespace BusinessLogic.Validators.Coupon;

public class CreateCouponDtoValidator : AbstractValidator<CreateCouponDto>
{
    public CreateCouponDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("کد تخفیف الزامی است.")
            .MaximumLength(50).WithMessage("کد تخفیف نباید بیش از ۵۰ کاراکتر باشد.");

        RuleFor(x => x.DiscountPercentage)
            .GreaterThan(0).WithMessage("درصد تخفیف باید بزرگتر از صفر باشد.")
            .LessThanOrEqualTo(100).WithMessage("درصد تخفیف نمی‌تواند بیش از ۱۰۰ باشد.");

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MaxDiscountAmount.HasValue)
            .WithMessage("سقف مبلغ تخفیف نمی‌تواند منفی باشد.");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue)
            .WithMessage("حداقل مبلغ سفارش نمی‌تواند منفی باشد.");

        RuleFor(x => x.UsageLimit)
            .GreaterThanOrEqualTo(0).When(x => x.UsageLimit.HasValue)
            .WithMessage("سقف تعداد استفاده نمی‌تواند منفی باشد.");

        RuleFor(x => x.UsageLimitPerUser)
            .GreaterThanOrEqualTo(0).When(x => x.UsageLimitPerUser.HasValue)
            .WithMessage("سقف استفادهٔ هر کاربر نمی‌تواند منفی باشد.");
    }
}

public class UpdateCouponDtoValidator : AbstractValidator<UpdateCouponDto>
{
    public UpdateCouponDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("کد تخفیف الزامی است.")
            .MaximumLength(50).WithMessage("کد تخفیف نباید بیش از ۵۰ کاراکتر باشد.");

        RuleFor(x => x.DiscountPercentage)
            .GreaterThan(0).WithMessage("درصد تخفیف باید بزرگتر از صفر باشد.")
            .LessThanOrEqualTo(100).WithMessage("درصد تخفیف نمی‌تواند بیش از ۱۰۰ باشد.");

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MaxDiscountAmount.HasValue)
            .WithMessage("سقف مبلغ تخفیف نمی‌تواند منفی باشد.");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue)
            .WithMessage("حداقل مبلغ سفارش نمی‌تواند منفی باشد.");

        RuleFor(x => x.UsageLimit)
            .GreaterThanOrEqualTo(0).When(x => x.UsageLimit.HasValue)
            .WithMessage("سقف تعداد استفاده نمی‌تواند منفی باشد.");

        RuleFor(x => x.UsageLimitPerUser)
            .GreaterThanOrEqualTo(0).When(x => x.UsageLimitPerUser.HasValue)
            .WithMessage("سقف استفادهٔ هر کاربر نمی‌تواند منفی باشد.");
    }
}
