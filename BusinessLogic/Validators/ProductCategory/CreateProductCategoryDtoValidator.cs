using BusinessLogic.DTOs.ProductCategory;
using FluentValidation;

namespace BusinessLogic.Validators.ProductCategory;

public class CreateProductCategoryDtoValidator : AbstractValidator<CreateProductCategoryDto>
{
    public CreateProductCategoryDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام دسته‌بندی الزامی است.")
            .MaximumLength(100).WithMessage("نام دسته‌بندی نباید بیش از ۱۰۰ کاراکتر باشد.");
    }
}
