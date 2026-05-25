using Application.Entities;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Validators.Products;
using FluentValidation.TestHelper;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

public class CreateProductValidatorTests
{
    private readonly CreateProductDtoValidator _validator = new();

    [Fact]
    public void Name_WhenEmpty_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { Name = "", Price = 100, SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Name_WhenMaxLengthExceeded_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { Name = new string('a', 201), Price = 100, SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Name_WhenValidLength_ShouldNotHaveError()
    {
        var dto = new CreateProductDto { Name = "Valid Product Name", Price = 100, SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Price_WhenZero_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { Price = 0, Name = "Test", SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Price_WhenNegative_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { Price = -10 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Price_WhenPositive_ShouldNotHaveError()
    {
        var dto = new CreateProductDto { Price = 100, Name = "Test", SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void SubcategoryId_WhenZero_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { SubcategoryId = 0, Name = "Test", Price = 100 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.SubcategoryId);
    }

    [Fact]
    public void SubcategoryId_WhenNegative_ShouldHaveValidationError()
    {
        var dto = new CreateProductDto { SubcategoryId = -5 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.SubcategoryId);
    }

    [Fact]
    public void SubcategoryId_WhenPositive_ShouldNotHaveError()
    {
        var dto = new CreateProductDto { SubcategoryId = 10, Name = "Test", Price = 100 };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveValidationErrorFor(x => x.SubcategoryId);
    }

    [Fact]
    public void AllValid_ShouldNotHaveAnyValidationErrors()
    {
        var dto = new CreateProductDto
        {
            Name = "Valid Product",
            Price = 100,
            SubcategoryId = 5,
            Unit = UnitOfMeasurement.Piece,
            Barcode = "123",
            Description = "Valid description"
        };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }
}