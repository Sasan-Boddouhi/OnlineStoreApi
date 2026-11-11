using BusinessLogic.DTOs.Shipping;
using FluentValidation;

namespace BusinessLogic.Validators.Shipping;

public class CreateShippingMethodDtoValidator : AbstractValidator<CreateShippingMethodDto>
{
    public CreateShippingMethodDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام روش ارسال الزامی است.")
            .MaximumLength(100).WithMessage("نام روش ارسال نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("شناسهٔ یکتای روش ارسال الزامی است.")
            .MaximumLength(100).WithMessage("شناسهٔ یکتای روش ارسال نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.CarrierName)
            .MaximumLength(100).WithMessage("نام حمل‌کننده نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.BaseCost)
            .GreaterThanOrEqualTo(0).WithMessage("هزینهٔ پایه نمی‌تواند منفی باشد.");

        RuleFor(x => x.CostPerKg)
            .GreaterThanOrEqualTo(0).WithMessage("هزینهٔ هر کیلوگرم نمی‌تواند منفی باشد.");

        RuleFor(x => x.EstimatedDays)
            .GreaterThanOrEqualTo(0).WithMessage("زمان تحویل نمی‌تواند منفی باشد.");
    }
}

public class UpdateShippingMethodDtoValidator : AbstractValidator<UpdateShippingMethodDto>
{
    public UpdateShippingMethodDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام روش ارسال الزامی است.")
            .MaximumLength(100).WithMessage("نام روش ارسال نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("شناسهٔ یکتای روش ارسال الزامی است.")
            .MaximumLength(100).WithMessage("شناسهٔ یکتای روش ارسال نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.CarrierName)
            .MaximumLength(100).WithMessage("نام حمل‌کننده نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.BaseCost)
            .GreaterThanOrEqualTo(0).WithMessage("هزینهٔ پایه نمی‌تواند منفی باشد.");

        RuleFor(x => x.CostPerKg)
            .GreaterThanOrEqualTo(0).WithMessage("هزینهٔ هر کیلوگرم نمی‌تواند منفی باشد.");

        RuleFor(x => x.EstimatedDays)
            .GreaterThanOrEqualTo(0).WithMessage("زمان تحویل نمی‌تواند منفی باشد.");
    }
}
