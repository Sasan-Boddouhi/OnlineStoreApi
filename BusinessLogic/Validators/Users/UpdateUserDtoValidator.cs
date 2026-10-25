// BusinessLogic/Validators/Users/UpdateUserDtoValidator.cs
using FluentValidation;
using BusinessLogic.DTOs.User;
using System.Globalization;
using BusinessLogic.Common.Validation;

namespace BusinessLogic.Validators.Users;

public class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0).WithMessage("شناسه کاربر معتبر نیست");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("نام الزامی است")
            .MaximumLength(100).WithMessage("نام نمی‌تواند بیش از 100 کاراکتر باشد");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("نام خانوادگی الزامی است")
            .MaximumLength(100).WithMessage("نام خانوادگی نمی‌تواند بیش از 100 کاراکتر باشد");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("شماره تماس الزامی است")
            .Matches(@"^09\d{9}$").WithMessage("شماره موبایل معتبر نیست (مثال: 09123456789)");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("ایمیل معتبر نیست");

        // اعتبارسنجی تاریخ شمسی (اختیاری)
        RuleFor(x => x.DateOfBirth)
            .Must(PersianDateValidator.IsValid).When(x => !string.IsNullOrEmpty(x.DateOfBirth))
            .WithMessage("فرمت تاریخ تولد باید YYYY/MM/DD باشد و تاریخ معتبری باشد");
    }
}