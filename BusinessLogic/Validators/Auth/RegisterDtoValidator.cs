using FluentValidation;
using BusinessLogic.DTOs.Auth;

namespace BusinessLogic.Validators.Auth
{
    public class RegisterDtoValidator : AbstractValidator<RegisterDto>
    {
        public RegisterDtoValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("نام الزامی است")
                .MaximumLength(100).WithMessage("نام نمی‌تواند بیش از 100 کاراکتر باشد");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("نام خانوادگی الزامی است")
                .MaximumLength(100).WithMessage("نام خانوادگی نمی‌تواند بیش از 100 کاراکتر باشد");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("شماره تماس الزامی است")
                .Matches(@"^09\d{9}$").WithMessage("شماره موبایل معتبر نیست (مثال: 09123456789)");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("رمز عبور الزامی است")
                .MinimumLength(6).WithMessage("رمز عبور باید حداقل 6 کاراکتر باشد");

            RuleFor(x => x.Email)
                .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
                .WithMessage("ایمیل معتبر نیست");

            RuleFor(x => x.DateOfBirth)
                .NotEmpty().WithMessage("تاریخ تولد الزامی است")
                .Matches(@"^\d{4}/\d{2}/\d{2}$").WithMessage("فرمت تاریخ تولد باید YYYY/MM/DD باشد");
        }
    }
}