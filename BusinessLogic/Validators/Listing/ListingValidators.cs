using BusinessLogic.DTOs.Listing;
using FluentValidation;

namespace BusinessLogic.Validators.Listing;

public class CreateSellerProductDtoValidator : AbstractValidator<CreateSellerProductDto>
{
    public CreateSellerProductDtoValidator()
    {
        RuleFor(x => x.VariantId)
            .GreaterThan(0).WithMessage("شناسه تنوع محصول باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("وضعیت پیشنهاد فروش معتبر نیست.");
    }
}

public class UpdateSellerProductDtoValidator : AbstractValidator<UpdateSellerProductDto>
{
    public UpdateSellerProductDtoValidator()
    {
        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("وضعیت پیشنهاد فروش معتبر نیست.");
    }
}
