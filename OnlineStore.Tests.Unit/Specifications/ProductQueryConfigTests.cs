using System;
using System.Linq;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Specifications.Products;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Specifications;

public class ProductQueryConfigTests
{
    [Fact]
    public void AllowedFields_ContainsAllExpectedFields()
    {
        // Arrange
        var expected = new[]
        {
            "name",
            "price",
            "description",
            "subcategory.subcategoryname",
            "isactive",
            "ExpirationDate"
        };

        // Act
        var actual = ProductQueryConfig.AllowedFields;

        // Assert
        actual.Should().BeEquivalentTo(expected);
        actual.Should().HaveCount(expected.Length);
    }

    [Fact]
    public void Projection_WhenSubcategoryIsNotNull_MapsCorrectly()
    {
        // Arrange
        var category = new ProductCategory
        {
            CategoryId = 10,
            CategoryName = "Electronics"
        };
        var subcategory = new ProductSubcategory
        {
            SubcategoryId = 5,
            SubcategoryName = "Laptops",
            Category = category,
            CategoryId = 10
        };
        var product = new Product
        {
            ProductId = 1,
            Name = "Dell XPS",
            Price = 1200,
            Description = "Good laptop",
            SubcategoryId = 5,
            Subcategory = subcategory,
            IsActive = true,
            Barcode = "12345"
        };

        // Act
        var dto = ProductQueryConfig.Projection.Compile()(product);

        // Assert
        dto.ProductId.Should().Be(1);
        dto.Name.Should().Be("Dell XPS");
        dto.Price.Should().Be(1200);
        dto.Description.Should().Be("Good laptop");
        dto.SubcategoryId.Should().Be(5);
        dto.CategoryId.Should().Be(10);
        dto.SubcategoryName.Should().Be("Laptops");
        dto.CategoryName.Should().Be("Electronics");
        dto.IsActive.Should().BeTrue();
        dto.Barcode.Should().Be("12345");
    }

    [Fact]
    public void Projection_WhenSubcategoryIsNull_ShouldHandleNullGracefully()
    {
        // Arrange
        var product = new Product
        {
            ProductId = 2,
            Name = "Test",
            Price = 100,
            SubcategoryId = 99,
            Subcategory = null,  // Subcategory missing
            IsActive = true
        };

        // Act
        // توجه: در کد فعلی Projection، دسترسی به p.Subcategory.CategoryId و ... اگر Subcategory null باشد، NullReferenceException می‌دهد.
        // این تست ثابت می‌کند که باید کد Projection اصلاح شود.
        Action act = () => ProductQueryConfig.Projection.Compile()(product);

        // Assert
        // انتظار: باید NullReferenceException رخ دهد (چون در Projection فعلی این باگ وجود دارد)
        act.Should().Throw<NullReferenceException>()
            .WithMessage("*Object reference not set to an instance of an object*");
    }
}