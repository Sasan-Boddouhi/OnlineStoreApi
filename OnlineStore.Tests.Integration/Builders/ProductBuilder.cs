using Application.Entities;

namespace OnlineStore.Tests.Integration.Builders;

public class ProductBuilder
{
    private readonly Product _product;

    public ProductBuilder()
    {
        // مقادیر پیش‌فرض برای پراپرتی‌های required
        _product = new Product
        {
            Name = "Default Product",
            Price = 1000m,
            Unit = UnitOfMeasurement.Piece,
            SubcategoryId = 1,       // باید در تست با مقدار واقعی جایگزین شود
            IsActive = true
        };
    }

    public ProductBuilder WithName(string name)
    {
        _product.Name = name;
        return this;
    }

    public ProductBuilder WithPrice(decimal price)
    {
        _product.Price = price;
        return this;
    }

    public ProductBuilder WithUnit(UnitOfMeasurement unit)
    {
        _product.Unit = unit;
        return this;
    }

    public ProductBuilder WithSubcategoryId(int subcategoryId)
    {
        _product.SubcategoryId = subcategoryId;
        return this;
    }

    public ProductBuilder WithDescription(string? description)
    {
        _product.Description = description;
        return this;
    }

    public ProductBuilder WithBarcode(string? barcode)
    {
        _product.Barcode = barcode;
        return this;
    }

    public ProductBuilder WithImageUrl(string? imageUrl)
    {
        _product.ImageUrl = imageUrl;
        return this;
    }

    public ProductBuilder WithExpirationDate(DateTime? expirationDate)
    {
        _product.ExpirationDate = expirationDate;
        return this;
    }

    public ProductBuilder SetActive(bool isActive)
    {
        _product.IsActive = isActive;
        return this;
    }

    public Product Build() => _product;
}