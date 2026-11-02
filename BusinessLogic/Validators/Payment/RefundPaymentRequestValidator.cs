using BusinessLogic.DTOs.Payment;
using FluentValidation;

namespace BusinessLogic.Validators.Payment;

public class RefundPaymentRequestValidator : AbstractValidator<RefundPaymentRequest>
{
    public RefundPaymentRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("دلیل برگشت وجه نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}
