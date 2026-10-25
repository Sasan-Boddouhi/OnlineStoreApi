using FluentValidation;
using BusinessLogic.DTOs.City;

namespace BusinessLogic.Validators.Cities;

public class CreateCityDtoValidator : AbstractValidator<CreateCityDto>
{
    public CreateCityDtoValidator()
    {
        RuleFor(x => x.CityName)
            .NotEmpty().WithMessage("نام شهر الزامی است")
            .MaximumLength(100).WithMessage("نام شهر حداکثر 100 کاراکتر است");

        RuleFor(x => x.ProvinceId)
            .GreaterThan(0).WithMessage("شناسه استان نامعتبر است");
    }
}