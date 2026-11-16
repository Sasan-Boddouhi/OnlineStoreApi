using FluentValidation;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.Validators.Common;

namespace BusinessLogic.Validators.Auth
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("شماره تماس الزامی است")
                .MustBeIranianMobile();

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("رمز عبور الزامی است");

            RuleFor(x => x.DeviceId)
                .NotEmpty().WithMessage("شناسه دستگاه الزامی است");
        }
    }
}