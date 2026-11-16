using Application.Common.Helpers;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Validators.Listing;
using BusinessLogic.Validators.Shipping;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

/// <summary>
/// Batch 45b (N6): قیمت پیشنهاد فروش و هزینه‌های روش ارسال باید ریال کامل باشند؛ ورودی کسری با 422 VALIDATION_ERROR رد می‌شود،
/// نه این‌که بعداً در checkout یا پرداخت مشتری به 409 AMOUNT_NOT_WHOLE_RIAL بخورد.
/// </summary>
public class Phase15Batch45bWholeRialValidatorTests
{
    [Theory]
    [InlineData(1_000.5)]
    [InlineData(0.01)]
    [InlineData(99_999.99)]
    public void CreateListing_FractionalPrice_Rejected(double price)
    {
        var result = new CreateSellerProductDtoValidator().Validate(new CreateSellerProductDto { VariantId = 1, Price = (decimal)price });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSellerProductDto.Price) && e.ErrorMessage == RialMoney.NotWholeMessage);
    }

    [Fact]
    public void CreateListing_WholePrice_Accepted()
        => new CreateSellerProductDtoValidator().Validate(new CreateSellerProductDto { VariantId = 1, Price = 1_000m }).IsValid.Should().BeTrue();

    [Fact]
    public void UpdateListing_FractionalPrice_Rejected_WholeAccepted()
    {
        var validator = new UpdateSellerProductDtoValidator();

        validator.Validate(new UpdateSellerProductDto { Price = 150.5m }).IsValid.Should().BeFalse();
        validator.Validate(new UpdateSellerProductDto { Price = 150m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateListingPrice_FractionalPrice_Rejected_WholeAccepted()
    {
        var validator = new UpdateListingPriceRequestValidator();

        validator.Validate(new UpdateListingPriceRequest { Price = 175.5m }).IsValid.Should().BeFalse();
        validator.Validate(new UpdateListingPriceRequest { Price = 0m }).IsValid.Should().BeFalse();
        validator.Validate(new UpdateListingPriceRequest { Price = 175m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateShipping_FractionalBaseCostOrCostPerKg_Rejected()
    {
        var validator = new CreateShippingMethodDtoValidator();

        validator.Validate(new CreateShippingMethodDto { Name = "Post", Slug = "post", BaseCost = 25_000.5m, CostPerKg = 0m })
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateShippingMethodDto { Name = "Post", Slug = "post", BaseCost = 25_000m, CostPerKg = 0.5m })
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateShippingMethodDto { Name = "Post", Slug = "post", BaseCost = 25_000m, CostPerKg = 1_000m })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateShipping_FractionalBaseCostOrCostPerKg_Rejected()
    {
        var validator = new UpdateShippingMethodDtoValidator();

        validator.Validate(new UpdateShippingMethodDto { Name = "Post", Slug = "post", BaseCost = 0.1m })
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateShippingMethodDto { Name = "Post", Slug = "post", CostPerKg = 999.99m })
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateShippingMethodDto { Name = "Post", Slug = "post", BaseCost = 30_000m, CostPerKg = 500m })
            .IsValid.Should().BeTrue();
    }
}
