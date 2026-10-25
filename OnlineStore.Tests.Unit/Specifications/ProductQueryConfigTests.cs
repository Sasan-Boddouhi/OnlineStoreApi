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
        var category = new ProductCategory { CategoryId = 5, CategoryName = "Gadgets" };
        var subcategory = new ProductSubcategory
        {
            SubcategoryId = 10,
            SubcategoryName = "Electronics",
            CategoryId = category.CategoryId,
            Category = category
        };
        var product = new Product
        {
            ProductId = 1,
            Name = "Test Product",
            Price = 99.99m,
            Description = "Test description",
            SubcategoryId = 10,
            Subcategory = subcategory,
            IsActive = true,
            Barcode = "12345"
        };

        // Act
        var dto = ProductQueryConfig.Projection.Compile()(product);

        // Assert
        dto.Should().NotBeNull();
        dto.Barcode.Should().Be("12345");
        dto.CategoryName.Should().Be("Gadgets");
        dto.SubcategoryName.Should().Be("Electronics");
    }

    [Fact]
    public void Projection_WhenSubcategoryIsNull_ShouldHandleNullGracefully()
    {
        // Arrange
        var product = new Product
        {
            ProductId = 2,
            Name = "Test Product",
            Price = 100,
            Description = "Test description",
            SubcategoryId = 99,
            Subcategory = null!,
            IsActive = true,
            Barcode = "12345"
        };

        // Act
        var dto = ProductQueryConfig.Projection.Compile()(product);

        // Assert
        dto.Should().NotBeNull();
        dto.SubcategoryName.Should().Be(string.Empty);
        dto.CategoryName.Should().Be(string.Empty);
        dto.CategoryId.Should().Be(0);
        dto.Barcode.Should().Be("12345");
    }
}