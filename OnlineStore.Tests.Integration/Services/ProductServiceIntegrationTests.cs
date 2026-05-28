using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Products;

public class ProductServiceIntegrationTests : BaseIntegrationTest
{
    private readonly IProductService _productService;

    public ProductServiceIntegrationTests(IntegrationTestFactory<Program> factory)
        : base(factory)
    {
        _productService = GetService<IProductService>();
    }

    [Fact]
    public async Task CreateProduct_Should_Create_Product_Successfully()
    {
        var db = GetService<AppDbContext>();

        var subcategory = db.ProductSubcategory.First();

        var dto = new CreateProductDto
        {
            Name = "Test Product",
            Price = 100,
            SubcategoryId = subcategory.SubcategoryId
        };

        var result = await _productService.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(dto.Name, result.Name);
        Assert.True(result.ProductId > 0);
    }

    [Fact]
    public async Task GetById_Should_Return_Product_When_Exists()
    {
        var db = GetService<AppDbContext>();
        var subcategory = db.ProductSubcategory.First();

        var created = await _productService.CreateAsync(new CreateProductDto
        {
            Name = "Product A",
            Price = 50,
            SubcategoryId = subcategory.SubcategoryId
        });

        var result = await _productService.GetByIdAsync(created.ProductId);

        Assert.NotNull(result);
        Assert.Equal(created.ProductId, result!.ProductId);
    }

    [Fact]
    public async Task UpdateProduct_Should_Update_Successfully()
    {
        var db = GetService<AppDbContext>();
        var subcategory = db.ProductSubcategory.First();

        var created = await _productService.CreateAsync(new CreateProductDto
        {
            Name = "Old Name",
            Price = 20,
            SubcategoryId = subcategory.SubcategoryId
        });

        var updated = await _productService.UpdateAsync(new UpdateProductDto
        {
            ProductId = created.ProductId,
            Name = "New Name",
            Price = 30,
            SubcategoryId = subcategory.SubcategoryId
        });

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
    }

    [Fact]
    public async Task DeleteProduct_Should_SoftDelete_Successfully()
    {
        var db = GetService<AppDbContext>();
        var subcategory = db.ProductSubcategory.First();

        var created = await _productService.CreateAsync(new CreateProductDto
        {
            Name = "To Delete",
            Price = 10,
            SubcategoryId = subcategory.SubcategoryId
        });

        var result = await _productService.DeleteAsync(created.ProductId);

        Assert.True(result);
    }
}