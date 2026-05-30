using System.Reflection;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Validators.ProductSubcategories;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace OnlineStore.Tests.Unit.Validators;

public class CreateProductSubcategoryDtoValidatorTests
{
    private readonly CreateProductSubcategoryDtoValidator _validator = new();

    private static CreateProductSubcategoryDto CreateDto(string subcategoryName = "Test", int categoryId = 1)
    {
        var dto = (CreateProductSubcategoryDto)Activator.CreateInstance(typeof(CreateProductSubcategoryDto), nonPublic: true)!;
        typeof(CreateProductSubcategoryDto).GetProperty("SubcategoryName")?.SetValue(dto, subcategoryName);
        typeof(CreateProductSubcategoryDto).GetProperty("CategoryId")?.SetValue(dto, categoryId);
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
    public void Should_Have_Error_When_CategoryId_Zero()
    {
        var dto = CreateDto(categoryId: 0);
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor("CategoryId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateDto();
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}