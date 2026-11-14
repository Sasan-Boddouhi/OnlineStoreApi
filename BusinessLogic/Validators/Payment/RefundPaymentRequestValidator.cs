using BusinessLogic.DTOs.Payment;
using FluentValidation;

namespace BusinessLogic.Validators.Payment;

public class RefundPaymentRequestValidator : AbstractValidator<RefundPaymentRequest>
{
    public RefundPaymentRequestValidator()
    {
        // Batch 42 (FIN-12): درخواست برگشت وجه بدون دلیل پذیرفته نمی‌شود (روی پرداخت و در AuditLog ثبت می‌شود).
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r) && r.Trim().Length >= 3).WithMessage("دلیل برگشت وجه الزامی است (دست‌کم ۳ کاراکتر).")
            .MaximumLength(500).WithMessage("دلیل برگشت وجه نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}
