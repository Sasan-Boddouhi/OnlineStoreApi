using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 49b (پاسخ مالک به Batch 49): برند محصول با <c>clearBrand: true</c> پاک می‌شود (BrandId = null)؛
/// فقط با همان سیاست <c>CanManageCatalog</c> (مجوز <c>catalog.products.write</c>) که از
/// <c>PUT products/{id}</c> محافظت می‌کند. <c>clearBrand</c> همراه با <c>brandId</c> → 422.
/// </summary>
public class Phase15Batch49bBrandClearTests : ControllerIntegrationTestBase
{
    public Phase15Batch49bBrandClearTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== پاک کردن برند ====================

    [Fact]
    public async Task UpdateProduct_WithClearBrand_SetsBrandIdToNull()
    {
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brandId: null, clearBrand: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ProductDto>())!.BrandId.Should().BeNull();
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProduct_WithClearBrand_ByManagerWithCatalogWrite_SetsBrandIdToNull()
    {
        // نقش Manager فقط catalog.products.write (و orders.read) دارد، نه admin.full.
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await RegisterUserAsync(roleCode: "Manager");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brandId: null, clearBrand: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProduct_WithClearBrandAndBrandId_Returns422_AndKeepsBrand()
    {
        var brand = await CreateBrandAsync();
        var other = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, other.BrandId, clearBrand: true));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        body.Should().Contain("ClearBrand");
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    [Fact]
    public async Task UpdateProduct_WithClearBrand_ByUserWithoutCatalogWrite_Returns403_AndKeepsBrand()
    {
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await RegisterUserAsync(roleCode: null);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brandId: null, clearBrand: true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    [Fact]
    public async Task UpdateProduct_WithClearBrandFalseAndNoBrandId_LeavesBrandUnchanged()
    {
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brandId: null, clearBrand: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    // ==================== ابزار ====================

    private AppDbContext Db() => GetService<AppDbContext>();

    private async Task<string> RegisterUserAsync(string? roleCode)
    {
        var phone = $"0913{Random.Shared.Next(1000000, 9999999):D7}";
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            PhoneNumber = phone,
            Password = "User@1234",
            FirstName = "Batch",
            LastName = "FortyNineB",
            DeviceId = $"b49b-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();

        if (roleCode is not null)
        {
            var db = Db();
            var user = await db.User.SingleAsync(u => u.PhoneNumber == phone);
            var role = await db.Role.SingleAsync(r => r.Code == roleCode);
            db.UserRole.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await db.SaveChangesAsync();

            // نقش‌ها از claimهای توکن خوانده می‌شوند؛ ورود دوباره توکنی با نقش تازه می‌دهد.
            var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                PhoneNumber = phone,
                Password = "User@1234",
                DeviceId = $"b49b-{Guid.NewGuid():N}"
            });
            login.EnsureSuccessStatusCode();
            auth = await login.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        }

        return auth!.AccessToken;
    }

    private async Task<Brand> CreateBrandAsync()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brand = new Brand { Name = "B49b-Brand-" + suffix, Slug = "b49b-brand-" + suffix, IsActive = true };
        db.Brand.Add(brand);
        await db.SaveChangesAsync();
        return brand;
    }

    private async Task<Product> CreateProductAsync(int? brandId)
    {
        var db = Db();
        var product = new Product
        {
            Name = "B49b-P-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            BrandId = brandId
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static object UpdateBody(Product product, int? brandId, bool clearBrand) => new
    {
        ProductId = product.ProductId,
        Name = product.Name,
        Price = product.Price,
        SubcategoryId = product.SubcategoryId,
        Description = "Batch 49b",
        BrandId = brandId,
        ClearBrand = clearBrand
    };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());
        return Client.SendAsync(request);
    }
}
