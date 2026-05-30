using System.Reflection;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Validators.ProductSubcategories;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class UpdateProductSubcategoryDtoValidatorTests
{
    private readonly UpdateProductSubcategoryDtoValidator _validator = new();

    private static UpdateProductSubcategoryDto CreateDto(int subcategoryId = 1, string subcategoryName = "Test", int categoryId = 1)
    {
        var dto = (UpdateProductSubcategoryDto)Activator.CreateInstance(typeof(UpdateProductSubcategoryDto), nonPublic: true)!;
        typeof(UpdateProductSubcategoryDto).GetProperty("SubcategoryId")?.SetValue(dto, subcategoryId);
        typeof(UpdateProductSubcategoryDto).GetProperty("SubcategoryName")?.SetValue(dto, subcategoryName);
        typeof(UpdateProductSubcategoryDto).GetProperty("CategoryId")?.SetValue(dto, categoryId);
        return dto;
    }

    [Fact]
    public void Should_Have_Error_When_Name_Empty()
    {
        var dto = CreateDto(subcategoryName: "");
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("SubcategoryName");
    }

    [Fact]
    public void Should_Have_Error_When_SubcategoryId_Zero()
    {
        var dto = CreateDto(subcategoryId: 0);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("SubcategoryId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateDto();
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}