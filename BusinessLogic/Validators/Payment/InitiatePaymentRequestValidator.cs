using BusinessLogic.DTOs.Payment;
using FluentValidation;

namespace BusinessLogic.Validators.Payment;

public class InitiatePaymentRequestValidator : AbstractValidator<InitiatePaymentRequest>
{
    public InitiatePaymentRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .GreaterThan(0).WithMessage("شناسه سفارش باید بزرگتر از صفر باشد.");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(256).WithMessage("کلید یکتایی نباید بیش از ۲۵۶ کاراکتر باشد.");
    }
}
