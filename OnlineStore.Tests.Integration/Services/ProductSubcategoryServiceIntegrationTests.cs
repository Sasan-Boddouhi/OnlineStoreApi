using Application.Entities;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class ProductSubcategoryServiceIntegrationTests : BaseIntegrationTest
{
    private IProductSubcategoryService SubcategoryService => GetService<IProductSubcategoryService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public ProductSubcategoryServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task CreateAsync_Valid_SubcategoryCreated()
    {
        var cat = await DbContext.ProductCategory.FirstAsync();
        var dto = new CreateProductSubcategoryDto
        {
            SubcategoryName = "NewSub",
            CategoryId = cat.CategoryId
        };
        var result = await SubcategoryService.CreateAsync(dto);
        result.Should().NotBeNull();
        result.SubcategoryId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_Updates()
    {
        var cat = await DbContext.ProductCategory.FirstAsync();
        var sub = new ProductSubcategory
        {
            SubcategoryName = "OldSub",
            CategoryId = cat.CategoryId
        };
        DbContext.ProductSubcategory.Add(sub);
        await DbContext.SaveChangesAsync();

        var dto = new UpdateProductSubcategoryDto
        {
            SubcategoryId = sub.SubcategoryId,
            SubcategoryName = "NewSubName",
            CategoryId = cat.CategoryId
        };
        var updated = await SubcategoryService.UpdateAsync(dto);
        updated.Should().NotBeNull();
        updated!.SubcategoryName.Should().Be("NewSubName");
    }

    [Fact]
    public async Task DeleteAsync_Existing_Deletes()
    {
        var cat = await DbContext.ProductCategory.FirstAsync();
        var sub = new ProductSubcategory
        {
            SubcategoryName = "ToDelete",
            CategoryId = cat.CategoryId
        };
        DbContext.ProductSubcategory.Add(sub);
        await DbContext.SaveChangesAsync();

        var result = await SubcategoryService.DeleteAsync(sub.SubcategoryId);
        result.Should().BeTrue();
    }
}