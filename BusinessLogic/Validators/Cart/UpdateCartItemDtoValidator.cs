using BusinessLogic.DTOs.Cart;
using FluentValidation;

namespace BusinessLogic.Validators.Cart;

public class UpdateCartItemDtoValidator : AbstractValidator<UpdateCartItemDto>
{
    public UpdateCartItemDtoValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("تعداد باید بزرگتر از صفر باشد.");
    }
}
