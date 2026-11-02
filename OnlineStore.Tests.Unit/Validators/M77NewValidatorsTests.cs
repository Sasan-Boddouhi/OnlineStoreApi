using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Validators.Cart;
using BusinessLogic.Validators.Payment;
using BusinessLogic.Validators.ProductCategory;
using BusinessLogic.Validators.Products;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

public class M77NewValidatorsTests
{
    [Theory]
    [InlineData("", "Street", "09123456789", false)]
    [InlineData("Name", "", "09123456789", false)]
    [InlineData("Name", "Street", "invalid-phone", false)]
    [InlineData("Name", "Street", "09123456789", true)]
    public void CheckoutDtoValidator_ValidatesProperly(string name, string address, string phone, bool isValid)
    {
        var validator = new CheckoutDtoValidator();
        var dto = new CheckoutDto
        {
            ShippingFullName = name,
            ShippingAddress = address,
            ShippingPhoneNumber = phone
        };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, -5, false)]
    [InlineData(1, 2, true)]
    public void AddCartItemDtoValidator_ValidatesProperly(int listingId, decimal quantity, bool isValid)
    {
        var validator = new AddCartItemDtoValidator();
        var dto = new AddCartItemDto { ListingId = listingId, Quantity = quantity };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1.5, true)]
    public void UpdateCartItemDtoValidator_ValidatesProperly(decimal quantity, bool isValid)
    {
        var validator = new UpdateCartItemDtoValidator();
        var dto = new UpdateCartItemDto { Quantity = quantity };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }

    [Theory]
    [InlineData(0, "Variant", false)]
    [InlineData(1, "", false)]
    [InlineData(1, "Variant", true)]
    public void CreateProductVariantDtoValidator_ValidatesProperly(int productId, string name, bool isValid)
    {
        var validator = new CreateProductVariantDtoValidator();
        var dto = new CreateProductVariantDto { ProductId = productId, Name = name };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(10, true)]
    public void InitiatePaymentRequestValidator_ValidatesProperly(int orderId, bool isValid)
    {
        var validator = new InitiatePaymentRequestValidator();
        var dto = new InitiatePaymentRequest { OrderId = orderId };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Electronics", true)]
    public void CreateProductCategoryDtoValidator_ValidatesProperly(string name, bool isValid)
    {
        var validator = new CreateProductCategoryDtoValidator();
        var dto = new CreateProductCategoryDto { Name = name };

        var result = validator.Validate(dto);
        result.IsValid.Should().Be(isValid);
    }
}
