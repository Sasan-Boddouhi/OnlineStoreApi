using BusinessLogic.DTOs.Product;
using FluentValidation;

namespace BusinessLogic.Validators.Products;

public class CreateProductVariantDtoValidator : AbstractValidator<CreateProductVariantDto>
{
    public CreateProductVariantDtoValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("شناسه محصول باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام تنوع محصول الزامی است.")
            .MaximumLength(150).WithMessage("نام تنوع محصول نباید بیش از ۱۵۰ کاراکتر باشد.");

        RuleFor(x => x.Sku)
            .MaximumLength(100).WithMessage("کد SKU نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.Barcode)
            .MaximumLength(100).WithMessage("بارکد نباید بیش از ۱۰۰ کاراکتر باشد.");
    }
}
