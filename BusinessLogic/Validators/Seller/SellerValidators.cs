using BusinessLogic.DTOs.Seller;
using FluentValidation;

namespace BusinessLogic.Validators.Seller;

public class CreateSellerDtoValidator : AbstractValidator<CreateSellerDto>
{
    public CreateSellerDtoValidator()
    {
        RuleFor(x => x.StoreName)
            .NotEmpty().WithMessage("نام فروشگاه الزامی است.")
            .MaximumLength(150).WithMessage("نام فروشگاه نباید بیش از ۱۵۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .MaximumLength(150).WithMessage("شناسهٔ یکتای فروشگاه نباید بیش از ۱۵۰ کاراکتر باشد.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("توضیحات نباید بیش از ۱۰۰۰ کاراکتر باشد.");
    }
}

public class UpdateSellerDtoValidator : AbstractValidator<UpdateSellerDto>
{
    public UpdateSellerDtoValidator()
    {
        RuleFor(x => x.StoreName)
            .NotEmpty().WithMessage("نام فروشگاه الزامی است.")
            .MaximumLength(150).WithMessage("نام فروشگاه نباید بیش از ۱۵۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .MaximumLength(150).WithMessage("شناسهٔ یکتای فروشگاه نباید بیش از ۱۵۰ کاراکتر باشد.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("توضیحات نباید بیش از ۱۰۰۰ کاراکتر باشد.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("وضعیت فروشنده معتبر نیست.");

        RuleFor(x => x.CommissionRate)
            .InclusiveBetween(0, 100).When(x => x.CommissionRate.HasValue)
            .WithMessage("نرخ کمیسیون باید بین ۰ و ۱۰۰ باشد.");
    }
}
