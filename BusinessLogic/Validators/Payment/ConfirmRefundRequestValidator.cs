using BusinessLogic.DTOs.Payment;
using FluentValidation;

namespace BusinessLogic.Validators.Payment;

/// <summary>Batch 42 (FIN-12): تأیید برگشت وجه بدون شمارهٔ پیگیری بانکی پذیرفته نمی‌شود.</summary>
public class ConfirmRefundRequestValidator : AbstractValidator<ConfirmRefundRequest>
{
    public ConfirmRefundRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.BankReference)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("شمارهٔ پیگیری بانکی برگشت وجه الزامی است.")
            .MaximumLength(100).WithMessage("شمارهٔ پیگیری بانکی نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.RefundedAtUtc)
            .Must(d => d is null || d.Value.ToUniversalTime() <= timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5))
            .WithMessage("تاریخ برگشت وجه نمی‌تواند در آینده باشد.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("یادداشت نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}

/// <summary>Batch 42 (FIN-12): بستن بررسی بدون شرح پذیرفته نمی‌شود.</summary>
public class ResolvePaymentReviewRequestValidator : AbstractValidator<ResolvePaymentReviewRequest>
{
    public ResolvePaymentReviewRequestValidator()
    {
        RuleFor(x => x.Note)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("شرح بررسی الزامی است.")
            .MaximumLength(500).WithMessage("شرح بررسی نباید بیش از ۵۰۰ کاراکتر باشد.");
    }
}
