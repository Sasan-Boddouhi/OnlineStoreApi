using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Media;
using DataLayer.Context;
using FluentAssertions;
using Online_Store_Application.Controllers;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class ProductImagesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public ProductImagesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<Product> CreateProductAsync()
    {
        var db = GetService<AppDbContext>();
        var prod = new Product
        {
            Name = "P-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(prod);
        await db.SaveChangesAsync();
        return prod;
    }

    [Fact]
    public async Task Get_AllowAnonymous_ReturnsOk()
    {
        var prod = await CreateProductAsync();
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync($"/api/v1/products/{prod.ProductId}/images");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddByUrl_Admin_ReturnsCreated()
    {
        var prod = await CreateProductAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new AddProductImageUrlRequest
        {
            Url = "https://example.com/img1.jpg",
            Alt = "Image 1",
            IsPrimary = true
        };

        var response = await Client.PostAsJsonAsync($"/api/v1/products/{prod.ProductId}/images/url", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AddByUrl_Unauthorized_Returns401()
    {
        var prod = await CreateProductAsync();
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new AddProductImageUrlRequest
        {
            Url = "https://example.com/img1.jpg",
            Alt = "Image 1"
        };

        var response = await Client.PostAsJsonAsync($"/api/v1/products/{prod.ProductId}/images/url", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_Admin_ReturnsNoContent()
    {
        var prod = await CreateProductAsync();
        var db = GetService<AppDbContext>();
        var img = new ProductImage
        {
            ProductId = prod.ProductId,
            Url = "https://example.com/del.jpg",
            IsPrimary = true
        };
        db.ProductImage.Add(img);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync($"/api/v1/products/{prod.ProductId}/images/{img.ProductImageId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
