using FluentValidation;
using BusinessLogic.DTOs.Employee;
using System;

namespace BusinessLogic.Validators.Employees;

public class CreateEmployeeDtoValidator : AbstractValidator<CreateEmployeeDto>
{
    public CreateEmployeeDtoValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0).WithMessage("شناسه کاربر نامعتبر است");

        RuleFor(x => x.EmployeeTypeId)
            .GreaterThan(0).WithMessage("شناسه نوع کارمند نامعتبر است");

        RuleFor(x => x.EmployeeNumber)
            .NotEmpty().WithMessage("شماره پرسنلی الزامی است")
            .MaximumLength(20).WithMessage("شماره پرسنلی حداکثر 20 کاراکتر است");

        RuleFor(x => x.Salary)
            .GreaterThan(0).WithMessage("حقوق باید بزرگتر از صفر باشد");

        // ✅ اعتبارسنجی HireDate (از نوع DateTime)
        RuleFor(x => x.HireDate)
            .Must(BeValidHireDate).WithMessage("تاریخ استخدام نباید از امروز بیشتر باشد");
    }

    private bool BeValidHireDate(DateTime hireDate)
    {
        return hireDate <= DateTime.UtcNow;
    }
}