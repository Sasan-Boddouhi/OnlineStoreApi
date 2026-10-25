using FluentValidation;
using BusinessLogic.DTOs.Auth;

namespace BusinessLogic.Validators.Auth
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("شماره تماس الزامی است")
                .Matches(@"^09\d{9}$").WithMessage("شماره موبایل معتبر نیست (مثال: 09123456789)");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("رمز عبور الزامی است");

            RuleFor(x => x.DeviceId)
                .NotEmpty().WithMessage("شناسه دستگاه الزامی است");
        }
    }
}