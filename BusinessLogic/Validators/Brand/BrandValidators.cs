using BusinessLogic.DTOs.Brand;
using FluentValidation;

namespace BusinessLogic.Validators.Brand;

public class CreateBrandDtoValidator : AbstractValidator<CreateBrandDto>
{
    public CreateBrandDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام برند الزامی است.")
            .MaximumLength(100).WithMessage("نام برند نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("شناسهٔ یکتای برند الزامی است.")
            .MaximumLength(120).WithMessage("شناسهٔ یکتای برند نباید بیش از ۱۲۰ کاراکتر باشد.");

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithMessage("آدرس لوگو نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}

public class UpdateBrandDtoValidator : AbstractValidator<UpdateBrandDto>
{
    public UpdateBrandDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام برند الزامی است.")
            .MaximumLength(100).WithMessage("نام برند نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("شناسهٔ یکتای برند الزامی است.")
            .MaximumLength(120).WithMessage("شناسهٔ یکتای برند نباید بیش از ۱۲۰ کاراکتر باشد.");

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithMessage("آدرس لوگو نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}
