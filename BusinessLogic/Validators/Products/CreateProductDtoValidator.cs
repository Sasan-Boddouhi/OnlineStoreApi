using FluentValidation;
using BusinessLogic.DTOs.Product;

namespace BusinessLogic.Validators.Products;

public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام محصول الزامی است")
            .MaximumLength(100).WithMessage("نام محصول حداکثر 100 کاراکتر می‌تواند باشد");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت محصول باید بزرگتر از صفر باشد");

        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).WithMessage("شناسه زیردسته‌بندی نامعتبر است");

        RuleFor(x => x.Description)
            .MaximumLength(250).WithMessage("توضیحات حداکثر 250 کاراکتر می‌تواند باشد")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Barcode)
            .MaximumLength(20).WithMessage("بارکد حداکثر 20 کاراکتر است")
            .When(x => !string.IsNullOrEmpty(x.Barcode));
    }
}