using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.Brand;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Validators.Auth;
using BusinessLogic.Validators.Brand;
using BusinessLogic.Validators.Cart;
using BusinessLogic.Validators.Common;
using BusinessLogic.Validators.Coupon;
using BusinessLogic.Validators.Dashboard;
using BusinessLogic.Validators.Listing;
using BusinessLogic.Validators.Payment;
using BusinessLogic.Validators.Products;
using BusinessLogic.Validators.Seller;
using BusinessLogic.Validators.Shipping;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

/// <summary>Batch 48 (F-25, SEC-12, PM-12, BE-08): each new rule has one accepted and one rejected case.</summary>
public class Phase15Batch48ValidatorTests
{
    private static string Len(int n) => new('x', n);

    // ---------- Coupon ----------
    private static CreateCouponDto ValidCoupon() => new() { Code = "SAVE10", DiscountPercentage = 10 };

    [Fact]
    public void Coupon_Valid_Accepted() => new CreateCouponDtoValidator().Validate(ValidCoupon()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(100.01)]
    [InlineData(150)]
    public void Coupon_PercentageOutOfRange_Rejected(double pct)
    {
        var dto = ValidCoupon();
        dto.DiscountPercentage = (decimal)pct;
        new CreateCouponDtoValidator().Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Coupon_Percentage100_Accepted()
    {
        var dto = ValidCoupon();
        dto.DiscountPercentage = 100;
        new CreateCouponDtoValidator().Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Coupon_NegativeAmounts_Rejected()
    {
        var a = ValidCoupon();
        a.MaxDiscountAmount = -1;
        var b = ValidCoupon();
        b.MinOrderAmount = -1;
        new CreateCouponDtoValidator().Validate(a).IsValid.Should().BeFalse();
        new CreateCouponDtoValidator().Validate(b).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Coupon_CodeLength50Accepted_51Rejected()
    {
        var ok = ValidCoupon();
        ok.Code = Len(50);
        var bad = ValidCoupon();
        bad.Code = Len(51);
        new CreateCouponDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CreateCouponDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateCoupon_AcceptsValid_RejectsNegativePercentage()
    {
        var ok = new UpdateCouponDto { Code = "A", DiscountPercentage = 5 };
        var bad = new UpdateCouponDto { Code = "A", DiscountPercentage = -5 };
        new UpdateCouponDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new UpdateCouponDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    // ---------- Shipping ----------
    private static CreateShippingMethodDto ValidShipping() => new() { Name = "Post", Slug = "post", BaseCost = 10, CostPerKg = 1 };

    [Fact]
    public void Shipping_Valid_Accepted() => new CreateShippingMethodDtoValidator().Validate(ValidShipping()).IsValid.Should().BeTrue();

    [Fact]
    public void Shipping_NegativeCosts_Rejected()
    {
        var a = ValidShipping();
        a.BaseCost = -1;
        var b = ValidShipping();
        b.CostPerKg = -1;
        new CreateShippingMethodDtoValidator().Validate(a).IsValid.Should().BeFalse();
        new CreateShippingMethodDtoValidator().Validate(b).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Shipping_LengthsAreColumnLengths()
    {
        var ok = ValidShipping();
        ok.Name = Len(100);
        ok.Slug = Len(100);
        ok.CarrierName = Len(100);
        var bad = ValidShipping();
        bad.CarrierName = Len(101);
        new CreateShippingMethodDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CreateShippingMethodDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateShipping_AcceptsValid_RejectsNegativeDays()
    {
        var ok = new UpdateShippingMethodDto { Name = "a", Slug = "a", BaseCost = 0, CostPerKg = 0, EstimatedDays = 2 };
        var bad = new UpdateShippingMethodDto { Name = "a", Slug = "a", EstimatedDays = -1 };
        new UpdateShippingMethodDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new UpdateShippingMethodDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    // ---------- Brand ----------
    [Fact]
    public void Brand_AcceptsValid_RejectsLongSlugAndEmptyName()
    {
        new CreateBrandDtoValidator().Validate(new CreateBrandDto { Name = "N", Slug = Len(120) }).IsValid.Should().BeTrue();
        new CreateBrandDtoValidator().Validate(new CreateBrandDto { Name = "N", Slug = Len(121) }).IsValid.Should().BeFalse();
        new CreateBrandDtoValidator().Validate(new CreateBrandDto { Name = "", Slug = "s" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateBrand_AcceptsValid_RejectsLongLogoUrl()
    {
        new UpdateBrandDtoValidator().Validate(new UpdateBrandDto { Name = "N", Slug = "s", LogoUrl = Len(500) }).IsValid.Should().BeTrue();
        new UpdateBrandDtoValidator().Validate(new UpdateBrandDto { Name = "N", Slug = "s", LogoUrl = Len(501) }).IsValid.Should().BeFalse();
    }

    // ---------- Variant ----------
    [Fact]
    public void UpdateVariant_AcceptsColumnLengths_RejectsOver()
    {
        var ok = new UpdateProductVariantDto { Name = Len(200), Sku = Len(64), Barcode = Len(20) };
        new UpdateProductVariantDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new UpdateProductVariantDtoValidator().Validate(new UpdateProductVariantDto { Sku = Len(65) }).IsValid.Should().BeFalse();
        new UpdateProductVariantDtoValidator().Validate(new UpdateProductVariantDto { Barcode = Len(21) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateVariant_SkuAndBarcodeMatchColumns()
    {
        var ok = new CreateProductVariantDto { ProductId = 1, Name = "v", Sku = Len(64), Barcode = Len(20) };
        new CreateProductVariantDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CreateProductVariantDtoValidator().Validate(new CreateProductVariantDto { ProductId = 1, Name = "v", Sku = Len(65) }).IsValid.Should().BeFalse();
        new CreateProductVariantDtoValidator().Validate(new CreateProductVariantDto { ProductId = 1, Name = "v", Barcode = Len(21) }).IsValid.Should().BeFalse();
    }

    // ---------- Product ----------
    [Fact]
    public void Product_NameMatchesColumn100()
    {
        var ok = new CreateProductDto { Name = Len(100), Price = 1, SubcategoryId = 1 };
        var bad = new CreateProductDto { Name = Len(101), Price = 1, SubcategoryId = 1 };
        new CreateProductDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CreateProductDtoValidator().Validate(bad).IsValid.Should().BeFalse();
        new UpdateProductDtoValidator().Validate(new UpdateProductDto { ProductId = 1, Name = Len(100), Price = 1, SubcategoryId = 1 }).IsValid.Should().BeTrue();
        new UpdateProductDtoValidator().Validate(new UpdateProductDto { ProductId = 1, Name = Len(101), Price = 1, SubcategoryId = 1 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Product_DescriptionAndBarcodeMatchColumns()
    {
        var ok = new CreateProductDto { Name = "n", Price = 1, SubcategoryId = 1, Description = Len(250), Barcode = Len(20) };
        new CreateProductDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        var d = new CreateProductDto { Name = "n", Price = 1, SubcategoryId = 1, Description = Len(251) };
        var b = new CreateProductDto { Name = "n", Price = 1, SubcategoryId = 1, Barcode = Len(21) };
        new CreateProductDtoValidator().Validate(d).IsValid.Should().BeFalse();
        new CreateProductDtoValidator().Validate(b).IsValid.Should().BeFalse();
    }

    // ---------- Payment / checkout keys ----------
    [Fact]
    public void PaymentIdempotencyKey_Max64()
    {
        new InitiatePaymentRequestValidator().Validate(new InitiatePaymentRequest { OrderId = 1, IdempotencyKey = Len(64) }).IsValid.Should().BeTrue();
        new InitiatePaymentRequestValidator().Validate(new InitiatePaymentRequest { OrderId = 1, IdempotencyKey = Len(65) }).IsValid.Should().BeFalse();
    }

    private static CheckoutDto ValidCheckout() => new() { ShippingFullName = "n", ShippingAddress = "a", ShippingPhoneNumber = "09123456789" };

    [Fact]
    public void CheckoutIdempotencyKey_Max100()
    {
        var ok = ValidCheckout();
        ok.IdempotencyKey = Len(100);
        var bad = ValidCheckout();
        bad.IdempotencyKey = Len(101);
        new CheckoutDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CheckoutDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CheckoutCouponCode_Max50()
    {
        var ok = ValidCheckout();
        ok.CouponCode = Len(50);
        var bad = ValidCheckout();
        bad.CouponCode = Len(51);
        new CheckoutDtoValidator().Validate(ok).IsValid.Should().BeTrue();
        new CheckoutDtoValidator().Validate(bad).IsValid.Should().BeFalse();
    }

    // ---------- Phone ----------
    [Theory]
    [InlineData("09123456789", true)]
    [InlineData("+989123456789", false)]
    [InlineData("9123456789", false)]
    [InlineData("0912345678", false)]
    [InlineData("091234567890", false)]
    [InlineData("08123456789", false)]
    [InlineData("0912345678a", false)]
    public void PhoneNumber_IsValid(string phone, bool expected) => PhoneNumber.IsValid(phone).Should().Be(expected);
    [Fact]
    public void Login_Register_Checkout_UseSharedPhoneRule()
    {
        new LoginDtoValidator().Validate(new LoginDto { PhoneNumber = "09123456789", Password = "p", DeviceId = "d" }).IsValid.Should().BeTrue();
        new LoginDtoValidator().Validate(new LoginDto { PhoneNumber = "0912", Password = "p", DeviceId = "d" }).IsValid.Should().BeFalse();
        var reg = new RegisterDto { FirstName = "a", LastName = "b", PhoneNumber = "0912", Password = "123456" };
        new RegisterDtoValidator().Validate(reg).IsValid.Should().BeFalse();
        var co = ValidCheckout();
        co.ShippingPhoneNumber = "+989123456789";
        new CheckoutDtoValidator().Validate(co).IsValid.Should().BeFalse();
    }

    // ---------- Ship seller group ----------
    [Fact]
    public void ShipSellerGroup_LengthsMatchColumns()
    {
        new ShipSellerGroupRequestValidator().Validate(new ShipSellerGroupRequest { CarrierName = Len(100), TrackingNumber = Len(100) }).IsValid.Should().BeTrue();
        new ShipSellerGroupRequestValidator().Validate(new ShipSellerGroupRequest { CarrierName = Len(101) }).IsValid.Should().BeFalse();
        new ShipSellerGroupRequestValidator().Validate(new ShipSellerGroupRequest { TrackingNumber = Len(101) }).IsValid.Should().BeFalse();
    }

    // ---------- Listing ----------
    [Fact]
    public void CreateListing_PriceMustBePositive()
    {
        new CreateSellerProductDtoValidator().Validate(new CreateSellerProductDto { VariantId = 1, Price = 1 }).IsValid.Should().BeTrue();
        new CreateSellerProductDtoValidator().Validate(new CreateSellerProductDto { VariantId = 1, Price = 0 }).IsValid.Should().BeFalse();
        new CreateSellerProductDtoValidator().Validate(new CreateSellerProductDto { VariantId = 0, Price = 1 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateListing_PriceMustBePositive()
    {
        new UpdateSellerProductDtoValidator().Validate(new UpdateSellerProductDto { Price = 5 }).IsValid.Should().BeTrue();
        new UpdateSellerProductDtoValidator().Validate(new UpdateSellerProductDto { Price = -5 }).IsValid.Should().BeFalse();
        new UpdateSellerProductDtoValidator().Validate(new UpdateSellerProductDto { Price = 0 }).IsValid.Should().BeFalse();
    }

    // ---------- Seller ----------
    [Fact]
    public void CreateSeller_RulesAcceptAndReject()
    {
        new CreateSellerDtoValidator().Validate(new CreateSellerDto { StoreName = Len(150), Slug = Len(150), Description = Len(1000) }).IsValid.Should().BeTrue();
        new CreateSellerDtoValidator().Validate(new CreateSellerDto { StoreName = "" }).IsValid.Should().BeFalse();
        new CreateSellerDtoValidator().Validate(new CreateSellerDto { StoreName = Len(151) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateSeller_CommissionRange()
    {
        new UpdateSellerDtoValidator().Validate(new UpdateSellerDto { StoreName = "s", CommissionRate = 100 }).IsValid.Should().BeTrue();
        new UpdateSellerDtoValidator().Validate(new UpdateSellerDto { StoreName = "s", CommissionRate = 101 }).IsValid.Should().BeFalse();
        new UpdateSellerDtoValidator().Validate(new UpdateSellerDto { StoreName = "", CommissionRate = 5 }).IsValid.Should().BeFalse();
    }

    // ---------- Https url ----------
    [Theory]
    [InlineData("https://example.com/a.jpg", true)]
    [InlineData("http://example.com/a.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/img/a.jpg", false)]
    [InlineData("//example.com/a.jpg", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("", false)]
    public void HttpsUrl_IsValid(string url, bool expected) => HttpsUrl.IsValid(url).Should().Be(expected);

    // ---------- Cart session ----------
    [Theory]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301", true)]
    [InlineData("abc", false)]
    [InlineData("session-3f2504e04f8941d39a0c0305e82c3301", false)]
    public void CartSessionId_MustBeGuid(string value, bool expected) => CartSessionId.IsValid(value).Should().Be(expected);
}
