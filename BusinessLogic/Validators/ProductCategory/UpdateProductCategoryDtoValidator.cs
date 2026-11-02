using BusinessLogic.DTOs.ProductCategory;
using FluentValidation;

namespace BusinessLogic.Validators.ProductCategory;

public class UpdateProductCategoryDtoValidator : AbstractValidator<UpdateProductCategoryDto>
{
    public UpdateProductCategoryDtoValidator()
    {
        RuleFor(x => x.ProductCategoryId)
            .GreaterThan(0).WithMessage("شناسه دسته‌بندی باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام دسته‌بندی الزامی است.")
            .MaximumLength(100).WithMessage("نام دسته‌بندی نباید بیش از ۱۰۰ کاراکتر باشد.");
    }
}
