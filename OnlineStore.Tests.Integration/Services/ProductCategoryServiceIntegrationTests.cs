using System.Reflection;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class ProductCategoryServiceIntegrationTests : BaseIntegrationTest
{
    private ProductCategoryService CategoryService =>
        (ProductCategoryService)GetService<IProductCategoryService>();

    public ProductCategoryServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task CreateAsync_AdminRole_CreatesCategory()
    {
        var dto = (CreateProductCategoryDto)Activator.CreateInstance(typeof(CreateProductCategoryDto), nonPublic: true)!;
        typeof(CreateProductCategoryDto).GetProperty("Name")?.SetValue(dto, "IntegrationCategory");

        var result = await CategoryService.CreateAsync(dto);
        result.Should().NotBeNull();

        var idProperty = result.GetType().GetProperty("ProductCategoryId") ?? result.GetType().GetProperty("CategoryId");
        idProperty.Should().NotBeNull();
        var id = (int)idProperty!.GetValue(result)!;
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateAsync_AdminRole_UpdatesCategory()
    {
        // Seed a category
        var createDto = (CreateProductCategoryDto)Activator.CreateInstance(typeof(CreateProductCategoryDto), nonPublic: true)!;
        typeof(CreateProductCategoryDto).GetProperty("Name")?.SetValue(createDto, "OriginalName");
        var created = await CategoryService.CreateAsync(createDto);

        var updateDto = (UpdateProductCategoryDto)Activator.CreateInstance(typeof(UpdateProductCategoryDto), nonPublic: true)!;
        typeof(UpdateProductCategoryDto).GetProperty("ProductCategoryId")?.SetValue(updateDto, created.ProductCategoryId);
        typeof(UpdateProductCategoryDto).GetProperty("Name")?.SetValue(updateDto, "UpdatedName");

        var result = await CategoryService.UpdateAsync(updateDto);

        result.Should().NotBeNull();
        result!.Name.Should().Be("UpdatedName");
        result.ProductCategoryId.Should().Be(created.ProductCategoryId);
    }
}