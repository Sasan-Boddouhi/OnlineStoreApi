using BusinessLogic.DTOs.Cart;
using FluentValidation;

namespace BusinessLogic.Validators.Cart;

public class AddCartItemDtoValidator : AbstractValidator<AddCartItemDto>
{
    public AddCartItemDtoValidator()
    {
        RuleFor(x => x.ListingId)
            .GreaterThan(0).WithMessage("شناسه پیشنهاد باید بزرگتر از صفر باشد.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("تعداد باید بزرگتر از صفر باشد.");
    }
}
