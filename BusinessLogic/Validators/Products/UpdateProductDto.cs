using FluentValidation;
using BusinessLogic.DTOs.Product;

namespace BusinessLogic.Validators.Products;

public class UpdateProductDtoValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductDtoValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("شناسه محصول معتبر نیست");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام محصول الزامی است")
            .MaximumLength(200);

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت محصول باید بزرگتر از صفر باشد");

        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).WithMessage("شناسه زیردسته‌بندی نامعتبر است");
    }
}