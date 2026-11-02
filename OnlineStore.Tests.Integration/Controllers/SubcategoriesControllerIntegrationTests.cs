using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.ProductSubcategory;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class SubcategoriesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public SubcategoriesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<ProductCategory> CreateCategoryAsync()
    {
        var db = GetService<AppDbContext>();
        var cat = new ProductCategory { CategoryName = "Cat-" + Guid.NewGuid().ToString("N")[..8] };
        db.ProductCategory.Add(cat);
        await db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task GetById_AllowAnonymous_ReturnsOk()
    {
        var cat = await CreateCategoryAsync();
        var db = GetService<AppDbContext>();
        var subcat = new ProductSubcategory { SubcategoryName = "Sub-" + Guid.NewGuid().ToString("N")[..8], CategoryId = cat.CategoryId };
        db.ProductSubcategory.Add(subcat);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync($"/api/v1/subcategories/{subcat.SubcategoryId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_Admin_ReturnsCreated()
    {
        var cat = await CreateCategoryAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProductSubcategoryDto
        {
            SubcategoryName = "NewSub-" + Guid.NewGuid().ToString("N")[..8],
            CategoryId = cat.CategoryId
        };

        var response = await Client.PostAsJsonAsync("/api/v1/subcategories", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var dto = new CreateProductSubcategoryDto
        {
            SubcategoryName = "UnauthSub",
            CategoryId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/v1/subcategories", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_Admin_ReturnsNoContent()
    {
        var cat = await CreateCategoryAsync();
        var db = GetService<AppDbContext>();
        var subcat = new ProductSubcategory { SubcategoryName = "OrigSub-" + Guid.NewGuid().ToString("N")[..8], CategoryId = cat.CategoryId };
        db.ProductSubcategory.Add(subcat);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new UpdateProductSubcategoryDto
        {
            SubcategoryId = subcat.SubcategoryId,
            SubcategoryName = "UpdSub-" + Guid.NewGuid().ToString("N")[..8],
            CategoryId = cat.CategoryId
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/subcategories/{subcat.SubcategoryId}", dto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_Admin_ReturnsNoContent()
    {
        var cat = await CreateCategoryAsync();
        var db = GetService<AppDbContext>();
        var subcat = new ProductSubcategory { SubcategoryName = "DelSub-" + Guid.NewGuid().ToString("N")[..8], CategoryId = cat.CategoryId };
        db.ProductSubcategory.Add(subcat);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync($"/api/v1/subcategories/{subcat.SubcategoryId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
