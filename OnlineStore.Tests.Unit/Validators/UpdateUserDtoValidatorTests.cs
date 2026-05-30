using System.Reflection;
using BusinessLogic.DTOs.User;
using BusinessLogic.Validators.Users;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class UpdateUserDtoValidatorTests
{
    private readonly UpdateUserDtoValidator _validator = new();

    private static UpdateUserDto CreateDto(int userId = 1, string phone = "09120000000", string firstName = "Ali", string lastName = "Rezaei")
    {
        var dto = (UpdateUserDto)Activator.CreateInstance(typeof(UpdateUserDto), nonPublic: true)!;
        typeof(UpdateUserDto).GetProperty("UserId")?.SetValue(dto, userId);
        typeof(UpdateUserDto).GetProperty("PhoneNumber")?.SetValue(dto, phone);
        typeof(UpdateUserDto).GetProperty("FirstName")?.SetValue(dto, firstName);
        typeof(UpdateUserDto).GetProperty("LastName")?.SetValue(dto, lastName);
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
    public void Should_Have_Error_When_UserId_Zero()
    {
        var dto = CreateDto(userId: 0);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("UserId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateDto();
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}