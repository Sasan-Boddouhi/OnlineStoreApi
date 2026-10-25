using System.Reflection;
using BusinessLogic.DTOs.City;
using BusinessLogic.Validators.Cities;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class CreateCityDtoValidatorTests
{
    private readonly CreateCityDtoValidator _validator = new();

    private static CreateCityDto CreateDto(string cityName, int provinceId)
    {
        var dto = (CreateCityDto)Activator.CreateInstance(typeof(CreateCityDto), nonPublic: true)!;
        typeof(CreateCityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        typeof(CreateCityDto).GetProperty("ProvinceId")?.SetValue(dto, provinceId);
        return dto;
    }

    [Fact]
    public void Should_Have_Error_When_CityName_Is_Empty()
    {
        var dto = CreateDto("", 1);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("CityName");
    }

    [Fact]
    public void Should_Have_Error_When_ProvinceId_Is_Zero()
    {
        var dto = CreateDto("Test", 0);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("ProvinceId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateDto("ValidCity", 1);
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}