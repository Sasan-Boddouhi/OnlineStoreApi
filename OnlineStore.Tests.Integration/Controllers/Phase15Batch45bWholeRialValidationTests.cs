using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 45b (N6): قیمت پیشنهاد فروش (ساخت، ویرایش، تغییر قیمت) و BaseCost / CostPerKg روش ارسال
/// با کسر ریال 422 VALIDATION_ERROR می‌گیرند و ذخیره نمی‌شوند. پیش‌تر ذخیره می‌شدند و مشتری بعداً در checkout
/// یا پرداخت به 409 AMOUNT_NOT_WHOLE_RIAL می‌خورد.
/// </summary>
public class Phase15Batch45bWholeRialValidationTests : ControllerIntegrationTestBase
{
    public Phase15Batch45bWholeRialValidationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private async Task AuthenticateAdminAsync()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string Token, Seller Seller)> CreateSellerAndTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Seller@123",
            FirstName = "B45b",
            LastName = "Seller",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var db = GetService<AppDbContext>();
        var user = await db.User.FirstAsync(u => u.PhoneNumber == phone);

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"B45b-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        return (auth!.AccessToken, seller);
    }

    private async Task<int> CreateVariantAsync()
    {
        var db = GetService<AppDbContext>();
        var product = new Product { Name = "B45b-" + Guid.NewGuid().ToString("N"), Price = 100, SubcategoryId = 1, Unit = UnitOfMeasurement.Kilogram };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", Sku = "SKU-" + Guid.NewGuid().ToString("N") };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();
        return variant.VariantId;
    }

    private async Task<SellerProduct> CreateListingAsync(Seller seller, int variantId)
    {
        var db = GetService<AppDbContext>();
        var listing = new SellerProduct { SellerId = seller.SellerId, VariantId = variantId, Price = 100, Status = SellerProductStatus.Active };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();
        return listing;
    }

    private async Task<decimal> ReloadPriceAsync(int listingId)
        => (await GetService<AppDbContext>().SellerProduct.AsNoTracking().FirstAsync(l => l.ListingId == listingId)).Price;

    [Fact]
    public async Task CreateListing_FractionalPrice_Returns422()
    {
        var (token, _) = await CreateSellerAndTokenAsync();
        var variantId = await CreateVariantAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsJsonAsync("/api/v1/listings", new { VariantId = variantId, Price = 12_345.5m, Status = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
        (await GetService<AppDbContext>().SellerProduct.AsNoTracking().AnyAsync(l => l.VariantId == variantId)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateListing_FractionalPrice_Returns422_AndKeepsThePrice()
    {
        var (token, seller) = await CreateSellerAndTokenAsync();
        var listing = await CreateListingAsync(seller, await CreateVariantAsync());
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PutAsJsonAsync($"/api/v1/listings/{listing.ListingId}", new { Price = 150.5m, Status = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
        (await ReloadPriceAsync(listing.ListingId)).Should().Be(100m);
    }

    [Fact]
    public async Task UpdateListingPrice_FractionalPrice_Returns422_AndKeepsThePrice()
    {
        var (token, seller) = await CreateSellerAndTokenAsync();
        var listing = await CreateListingAsync(seller, await CreateVariantAsync());
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PatchAsJsonAsync($"/api/v1/listings/{listing.ListingId}/price", new { Price = 175.25m });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
        (await ReloadPriceAsync(listing.ListingId)).Should().Be(100m);
    }

    [Theory]
    [InlineData(25_000.5, 0)]
    [InlineData(25_000, 0.5)]
    public async Task CreateShippingMethod_FractionalCost_Returns422(double baseCost, double costPerKg)
    {
        await AuthenticateAdminAsync();
        var slug = "b45b-" + Guid.NewGuid().ToString("N")[..8];

        var response = await Client.PostAsJsonAsync("/api/v1/shipping-methods", new
        {
            Name = "B45b " + Guid.NewGuid().ToString("N")[..6],
            Slug = slug,
            BaseCost = (decimal)baseCost,
            CostPerKg = (decimal)costPerKg,
            EstimatedDays = 3,
            IsActive = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
        (await GetService<AppDbContext>().ShippingMethod.AsNoTracking().AnyAsync(m => m.Slug == slug)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateShippingMethod_FractionalBaseCost_Returns422()
    {
        var db = GetService<AppDbContext>();
        var method = new ShippingMethod { Name = "B45b Post", Slug = "b45b-" + Guid.NewGuid().ToString("N")[..8], BaseCost = 25_000m, IsActive = true };
        db.ShippingMethod.Add(method);
        await db.SaveChangesAsync();
        await AuthenticateAdminAsync();

        var response = await Client.PutAsJsonAsync($"/api/v1/shipping-methods/{method.ShippingMethodId}", new
        {
            method.Name,
            method.Slug,
            BaseCost = 30_000.75m,
            CostPerKg = 0m,
            EstimatedDays = 3,
            IsActive = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
        (await db.ShippingMethod.AsNoTracking().FirstAsync(m => m.ShippingMethodId == method.ShippingMethodId)).BaseCost.Should().Be(25_000m);
    }
}
