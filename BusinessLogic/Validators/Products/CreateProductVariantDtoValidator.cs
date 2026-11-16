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
            .MaximumLength(200).WithMessage("نام تنوع محصول نباید بیش از ۲۰۰ کاراکتر باشد.");

        RuleFor(x => x.Sku)
            .MaximumLength(64).WithMessage("کد SKU نباید بیش از ۶۴ کاراکتر باشد.");

        RuleFor(x => x.Barcode)
            .MaximumLength(20).WithMessage("بارکد نباید بیش از ۲۰ کاراکتر باشد.");

        RuleForEach(x => x.Attributes).ChildRules(a =>
        {
            a.RuleFor(v => v.AttributeId).GreaterThan(0).WithMessage("شناسه ویژگی نامعتبر است.");
            a.RuleFor(v => v.Value).MaximumLength(200).WithMessage("مقدار ویژگی نباید بیش از ۲۰۰ کاراکتر باشد.");
        });
    }
}
