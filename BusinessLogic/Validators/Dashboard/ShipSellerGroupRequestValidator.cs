using BusinessLogic.DTOs.Dashboard;
using FluentValidation;

namespace BusinessLogic.Validators.Dashboard;

public class ShipSellerGroupRequestValidator : AbstractValidator<ShipSellerGroupRequest>
{
    public ShipSellerGroupRequestValidator()
    {
        RuleFor(x => x.CarrierName)
            .MaximumLength(100).WithMessage("نام حمل‌کننده نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.TrackingNumber)
            .MaximumLength(100).WithMessage("کد رهگیری نباید بیش از ۱۰۰ کاراکتر باشد.");
    }
}
