using FluentValidation;
using BusinessLogic.DTOs.ProductSubcategory;

namespace BusinessLogic.Validators.ProductSubcategories;

public class UpdateProductSubcategoryDtoValidator : AbstractValidator<UpdateProductSubcategoryDto>
{
    public UpdateProductSubcategoryDtoValidator()
    {
        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).WithMessage("شناسه زیردسته نامعتبر است");

        RuleFor(x => x.SubcategoryName)
            .NotEmpty().WithMessage("نام زیردسته الزامی است")
            .MaximumLength(100).WithMessage("نام زیردسته حداکثر 100 کاراکتر است");

        RuleFor(x => x.Description)
            .MaximumLength(250).WithMessage("توضیحات حداکثر 250 کاراکتر است");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("شناسه دسته بندی اصلی نامعتبر است");
    }
}
