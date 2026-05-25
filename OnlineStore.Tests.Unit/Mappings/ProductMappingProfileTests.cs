using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Profiles;
using Application.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace OnlineStore.Tests.Unit.Mappings;

public class ProductMappingProfileTests
{
    private readonly IMapper _mapper;

    public ProductMappingProfileTests()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<ProductProfile>(),
            NullLoggerFactory.Instance
        );
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Product_To_ProductDto_MapsCorrectly()
    {
        // Arrange
        var product = new Product
        {
            ProductId = 1,
            Name = "Laptop",
            Price = 1200,
            Description = "Good laptop",
            SubcategoryId = 10,
            IsActive = true,
            // Unit = UnitOfMeasurement.Piece,  // Unit در DTO نیست، نیازی به مقداردهی نیست
            Barcode = "12345",
            ImageUrl = "http://example.com/img.jpg",
            ExpirationDate = DateTime.UtcNow.AddDays(30)
        };

        // Act
        var dto = _mapper.Map<ProductDto>(product);

        // Assert
        dto.ProductId.Should().Be(product.ProductId);
        dto.Name.Should().Be(product.Name);
        dto.Price.Should().Be(product.Price);
        dto.Description.Should().Be(product.Description);
        dto.SubcategoryId.Should().Be(product.SubcategoryId);
        dto.IsActive.Should().Be(product.IsActive);
        dto.Barcode.Should().Be(product.Barcode);
        dto.ImageUrl.Should().Be(product.ImageUrl);
        dto.ExpirationDate.Should().Be(product.ExpirationDate);

        // فیلدهای اضافی (از ناوبری) – فعلاً بررسی نمی‌کنیم یا انتظار null داریم
        dto.SubcategoryName.Should().BeNull();   // چون Subcategory مقدار ندارد
        dto.CategoryId.Should().Be(0);
        dto.CategoryName.Should().BeNull();
    }

    [Fact]
    public void CreateProductDto_To_Product_MapsCorrectly()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "New Product",
            Price = 250,
            SubcategoryId = 5,
            Unit = UnitOfMeasurement.Kilogram,
            Barcode = "BAR001",
            ImageUrl = "http://example.com/new.jpg",
            Description = "New desc",
            ExpirationDate = DateTime.UtcNow.AddDays(60)
        };

        // Act
        var product = _mapper.Map<Product>(dto);

        // Assert
        product.Name.Should().Be(dto.Name);
        product.Price.Should().Be(dto.Price);
        product.SubcategoryId.Should().Be(dto.SubcategoryId);
        product.Unit.Should().Be(dto.Unit);
        product.Barcode.Should().Be(dto.Barcode);
        product.ImageUrl.Should().Be(dto.ImageUrl);
        product.Description.Should().Be(dto.Description);
        product.ExpirationDate.Should().Be(dto.ExpirationDate);

        // مقادیر پیش‌فرض
        product.IsActive.Should().BeTrue();
        product.ProductId.Should().Be(0);
    }

    [Fact]
    public void UpdateProductDto_To_Product_MapsCorrectly()
    {
        // Arrange
        var dto = new UpdateProductDto
        {
            ProductId = 99,
            Name = "Updated Name",
            Price = 500,
            SubcategoryId = 8,
            Description = "Updated desc"
        };

        // Act
        var product = _mapper.Map<Product>(dto);

        // Assert
        product.ProductId.Should().Be(dto.ProductId);
        product.Name.Should().Be(dto.Name);
        product.Price.Should().Be(dto.Price);
        product.SubcategoryId.Should().Be(dto.SubcategoryId);
        product.Description.Should().Be(dto.Description);

        // سایر فیلدهای Product باید مقادیر پیش‌فرض خود را داشته باشند
        product.Unit.Should().Be(UnitOfMeasurement.Piece); // یا هر پیش‌فرض دیگری
        product.IsActive.Should().BeTrue();
        product.Barcode.Should().BeNull();
        product.ImageUrl.Should().BeNull();
        product.ExpirationDate.Should().BeNull();
    }

    [Fact]
    public void NullableProperties_WhenNull_MapsToNull()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "Test",
            Price = 100,
            SubcategoryId = 1,
            Description = null,
            Barcode = null,
            ImageUrl = null,
            ExpirationDate = null
        };

        // Act
        var product = _mapper.Map<Product>(dto);

        // Assert
        product.Description.Should().BeNull();
        product.Barcode.Should().BeNull();
        product.ImageUrl.Should().BeNull();
        product.ExpirationDate.Should().BeNull();
    }
}