using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Listing;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class ListingsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public ListingsControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(string Token, int UserId, Seller Seller)> CreateSellerAndTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Seller@123",
            FirstName = "Seller",
            LastName = "User",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", registerData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var db = GetService<AppDbContext>();
        var user = await db.User.FirstAsync(u => u.PhoneNumber == phone);

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"Store-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        return (result!.AccessToken, user.UserId, seller);
    }

    private async Task<(Product Product, ProductVariant Variant)> CreateProductAndVariantAsync()
    {
        var db = GetService<AppDbContext>();
        var product = new Product
        {
            Name = "P-" + Guid.NewGuid().ToString("N"),
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.ProductId,
            Name = "Default",
            Sku = "SKU-" + Guid.NewGuid().ToString("N")
        };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();

        return (product, variant);
    }

    [Fact]
    public async Task Update_OwnerSeller_ReturnsOk()
    {
        var (token1, _, seller1) = await CreateSellerAndTokenAsync();
        var (_, variant) = await CreateProductAndVariantAsync();

        var db = GetService<AppDbContext>();
        var listing = new SellerProduct
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var updateDto = new UpdateSellerProductDto
        {
            Price = 150,
            Status = SellerProductStatus.Active
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/listings/{listing.ListingId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SellerProductDto>();
        updated.Should().NotBeNull();
        updated!.Price.Should().Be(150);
    }

    [Fact]
    public async Task Update_ForeignSeller_ReturnsForbidden()
    {
        var (_, _, seller1) = await CreateSellerAndTokenAsync();
        var (token2, _, _) = await CreateSellerAndTokenAsync();
        var (_, variant) = await CreateProductAndVariantAsync();

        var db = GetService<AppDbContext>();
        var listing = new SellerProduct
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        // Seller 2 attempts to update Seller 1's listing -> IDOR protection
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var updateDto = new UpdateSellerProductDto
        {
            Price = 200,
            Status = SellerProductStatus.Active
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/listings/{listing.ListingId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePrice_OwnerSeller_ReturnsOk()
    {
        var (token1, _, seller1) = await CreateSellerAndTokenAsync();
        var (_, variant) = await CreateProductAndVariantAsync();

        var db = GetService<AppDbContext>();
        var listing = new SellerProduct
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var patchDto = new UpdateListingPriceRequest
        {
            Price = 175
        };

        var response = await Client.PatchAsJsonAsync($"/api/v1/listings/{listing.ListingId}/price", patchDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SellerProductDto>();
        updated.Should().NotBeNull();
        updated!.Price.Should().Be(175);
    }

    [Fact]
    public async Task Delete_OwnerSeller_ReturnsNoContent()
    {
        var (token1, _, seller1) = await CreateSellerAndTokenAsync();
        var (_, variant) = await CreateProductAndVariantAsync();

        var db = GetService<AppDbContext>();
        var listing = new SellerProduct
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var response = await Client.DeleteAsync($"/api/v1/listings/{listing.ListingId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
