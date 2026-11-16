using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Online_Store_Application.Controllers;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>Batch 48 (F-25, SEC-12, PM-12): invalid input answers 422 VALIDATION_ERROR / CART_SESSION_INVALID.</summary>
public class Phase15Batch48ValidationTests : ControllerIntegrationTestBase
{
    public Phase15Batch48ValidationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task AuthenticateAdminAsync()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    [Fact]
    public async Task CreateCoupon_Percentage150_Returns422()
    {
        await AuthenticateAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/coupons", new
        {
            Code = "B48-" + Guid.NewGuid().ToString("N")[..8],
            DiscountPercentage = 150
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task CreateShippingMethod_NegativeCost_Returns422()
    {
        await AuthenticateAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/shipping-methods", new
        {
            Name = "B48 " + Guid.NewGuid().ToString("N")[..6],
            Slug = "b48-" + Guid.NewGuid().ToString("N")[..8],
            BaseCost = -5,
            CostPerKg = 0,
            EstimatedDays = 3,
            IsActive = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetCart_NonGuidSessionHeader_Returns422()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Cart-Session");
        Client.DefaultRequestHeaders.Add("X-Cart-Session", "abc");

        var response = await Client.GetAsync("/api/v1/carts/current");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("CART_SESSION_INVALID");
    }

    [Fact]
    public async Task GetCart_GuidSessionHeader_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Cart-Session");
        Client.DefaultRequestHeaders.Add("X-Cart-Session", Guid.NewGuid().ToString());

        var response = await Client.GetAsync("/api/v1/carts/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/a.jpg")]
    [InlineData("/images/a.jpg")]
    public async Task AddProductImageByUrl_NonHttpsUrl_Returns422(string url)
    {
        var db = GetService<AppDbContext>();
        var product = new Product
        {
            Name = "B48-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();
        await AuthenticateAdminAsync();

        var response = await Client.PostAsJsonAsync(
            $"/api/v1/products/{product.ProductId}/images/url",
            new AddProductImageUrlRequest { Url = url, Alt = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
    }
}
