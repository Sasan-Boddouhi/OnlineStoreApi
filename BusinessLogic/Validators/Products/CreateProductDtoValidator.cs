using FluentValidation;
using BusinessLogic.DTOs.Product;

namespace BusinessLogic.Validators.Products;

public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام محصول الزامی است")
            .MaximumLength(200).WithMessage("نام محصول حداکثر 200 کاراکتر می‌تواند باشد");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت محصول باید بزرگتر از صفر باشد");

        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).WithMessage("شناسه زیردسته‌بندی نامعتبر است");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("توضیحات حداکثر 1000 کاراکتر می‌تواند باشد")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Barcode)
            .MaximumLength(50).WithMessage("بارکد حداکثر 50 کاراکتر است")
            .When(x => !string.IsNullOrEmpty(x.Barcode));
    }
}