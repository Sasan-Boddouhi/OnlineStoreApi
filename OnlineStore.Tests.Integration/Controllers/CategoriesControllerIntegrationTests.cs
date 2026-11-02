using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.ProductCategory;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class CategoriesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public CategoriesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetAll_AllowAnonymous_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync("/api/v1/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AllowAnonymous_ReturnsOk()
    {
        var db = GetService<AppDbContext>();
        var cat = new ProductCategory { CategoryName = "Cat-" + Guid.NewGuid().ToString("N")[..8] };
        db.ProductCategory.Add(cat);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync($"/api/v1/categories/{cat.CategoryId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_Admin_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProductCategoryDto
        {
            Name = "NewCat-" + Guid.NewGuid().ToString("N")[..8]
        };

        var response = await Client.PostAsJsonAsync("/api/v1/categories", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var dto = new CreateProductCategoryDto
        {
            Name = "UnauthCat"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/categories", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_Admin_ReturnsNoContent()
    {
        var db = GetService<AppDbContext>();
        var cat = new ProductCategory { CategoryName = "OrigCat-" + Guid.NewGuid().ToString("N")[..8] };
        db.ProductCategory.Add(cat);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new UpdateProductCategoryDto
        {
            ProductCategoryId = cat.CategoryId,
            Name = "UpdCat-" + Guid.NewGuid().ToString("N")[..8]
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/categories/{cat.CategoryId}", dto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_Admin_ReturnsNoContent()
    {
        var db = GetService<AppDbContext>();
        var cat = new ProductCategory { CategoryName = "DelCat-" + Guid.NewGuid().ToString("N")[..8] };
        db.ProductCategory.Add(cat);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync($"/api/v1/categories/{cat.CategoryId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
