// BusinessLogic/Validators/Users/CreateUserDtoValidator.cs
using FluentValidation;
using BusinessLogic.DTOs.User;
using System.Globalization;

namespace BusinessLogic.Validators.Users;

public class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserDtoValidator()
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

        // اعتبارسنجی تاریخ شمسی
        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("تاریخ تولد الزامی است")
            .Must(BeValidPersianDate).WithMessage("فرمت تاریخ تولد باید YYYY/MM/DD باشد و تاریخ معتبری باشد");
    }

    private bool BeValidPersianDate(string? persianDate)
    {
        if (string.IsNullOrWhiteSpace(persianDate))
            return false;

        if (!System.Text.RegularExpressions.Regex.IsMatch(persianDate, @"^\d{4}/\d{2}/\d{2}$"))
            return false;

        var parts = persianDate.Split('/');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out int year) ||
            !int.TryParse(parts[1], out int month) ||
            !int.TryParse(parts[2], out int day))
            return false;

        if (year < 1300 || year > 1500)
            return false;

        try
        {
            var pc = new PersianCalendar();
            var date = pc.ToDateTime(year, month, day, 0, 0, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}