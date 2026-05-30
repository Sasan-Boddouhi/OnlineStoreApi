using System.Reflection;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.Validators.Employees;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class CreateEmployeeDtoValidatorTests
{
    private readonly CreateEmployeeDtoValidator _validator = new();

    private static CreateEmployeeDto CreateDto(string? employeeNumber = "E-001", int userId = 1, int employeeTypeId = 1, decimal salary = 5000)
    {
        var dto = (CreateEmployeeDto)Activator.CreateInstance(typeof(CreateEmployeeDto), nonPublic: true)!;
        typeof(CreateEmployeeDto).GetProperty("EmployeeNumber")?.SetValue(dto, employeeNumber);
        typeof(CreateEmployeeDto).GetProperty("UserId")?.SetValue(dto, userId);
        typeof(CreateEmployeeDto).GetProperty("EmployeeTypeId")?.SetValue(dto, employeeTypeId);
        typeof(CreateEmployeeDto).GetProperty("Salary")?.SetValue(dto, salary);
        return dto;
    }

    [Fact]
    public void Should_Have_Error_When_EmployeeNumber_Empty()
    {
        var dto = CreateDto(employeeNumber: "");
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("EmployeeNumber");
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