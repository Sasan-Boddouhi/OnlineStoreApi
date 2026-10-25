using System.Reflection;
using BusinessLogic.DTOs.User;
using BusinessLogic.Validators.Users;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class CreateUserDtoValidatorTests
{
    private readonly CreateUserDtoValidator _validator = new();

    private static CreateUserDto CreateDto(
        string phone = "09120000000",
        string password = "Strong@123",
        string firstName = "Ali",
        string lastName = "Rezaei",
        string dateOfBirth = "1370/01/01")
    {
        var dto = (CreateUserDto)Activator.CreateInstance(typeof(CreateUserDto), nonPublic: true)!;
        typeof(CreateUserDto).GetProperty("PhoneNumber")?.SetValue(dto, phone);
        typeof(CreateUserDto).GetProperty("Password")?.SetValue(dto, password);
        typeof(CreateUserDto).GetProperty("FirstName")?.SetValue(dto, firstName);
        typeof(CreateUserDto).GetProperty("LastName")?.SetValue(dto, lastName);
        typeof(CreateUserDto).GetProperty("DateOfBirth")?.SetValue(dto, dateOfBirth);
        return dto;
    }

    [Fact]
    public void Should_Have_Error_When_Phone_Empty()
    {
        var dto = CreateDto(phone: "");
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("PhoneNumber");
    }

    [Fact]
    public void Should_Have_Error_When_Password_Weak()
    {
        var dto = CreateDto(password: "123");
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("Password");
    }

    [Fact]
    public void Should_Have_Error_When_DateOfBirth_Invalid()
    {
        var dto = CreateDto(dateOfBirth: "invalid");
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("DateOfBirth");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateDto();
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}