using BusinessLogic.DTOs.Auth;
using BusinessLogic.Validators.Auth;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Validators.Products;
using FluentValidation.TestHelper;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

public class RegisterDtoValidatorTests
{
    private readonly RegisterDtoValidator _validator = new();

    [Fact]
    public void ValidRegisterDto_PassesValidation()
    {
        var dto = new RegisterDto
        {
            FirstName = "John",
            LastName = "Doe",
            PhoneNumber = "09123456789",
            Password = "123456",
            DateOfBirth = "1370/01/01"
        };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void InvalidPhoneNumber_FailsValidation()
    {
        var dto = new RegisterDto { PhoneNumber = "12345" };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }
}

public class CreateProductDtoValidatorTests
{
    private readonly CreateProductDtoValidator _validator = new();

    [Fact]
    public void ValidProduct_Passes()
    {
        var dto = new CreateProductDto { Name = "Laptop", Price = 1000, SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PriceZero_Fails()
    {
        var dto = new CreateProductDto { Name = "Laptop", Price = 0, SubcategoryId = 1 };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }
}