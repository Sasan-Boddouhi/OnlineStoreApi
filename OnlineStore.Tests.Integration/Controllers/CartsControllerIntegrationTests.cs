using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Cart;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class CartsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public CartsControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(string Token, int UserId, SellerProduct Listing)> SeedUserAndListingAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Cart",
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
            StoreName = "CartStore-" + Guid.NewGuid().ToString("N")[..8],
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "CartP-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.ProductId,
            Name = "V",
            Sku = "SKU-" + Guid.NewGuid().ToString("N")[..8]
        };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        var province = new Province { ProvinceName = "P-" + Guid.NewGuid().ToString("N")[..8] };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var city = new City { CityName = "C-" + Guid.NewGuid().ToString("N")[..8], ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "A",
            RecipientLastName = "B",
            UserId = user.UserId
        };
        db.Address.Add(address);
        await db.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = "W-" + Guid.NewGuid().ToString("N")[..8],
            AddressId = address.AddressId,
            SellerId = seller.SellerId
        };
        db.Warehouse.Add(warehouse);
        await db.SaveChangesAsync();

        var inventory = new Inventory(listing.ListingId, warehouse.WarehouseId, 100);
        db.Inventory.Add(inventory);
        await db.SaveChangesAsync();

        return (result!.AccessToken, user.UserId, listing);
    }

    [Fact]
    public async Task GetCurrent_AllowAnonymous_WithSession_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Cart-Session");
        Client.DefaultRequestHeaders.Add("X-Cart-Session", $"session-{Guid.NewGuid():N}");

        var response = await Client.GetAsync("/api/v1/carts/current");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddItem_AnonymousWithSession_ReturnsOk()
    {
        var (_, _, listing) = await SeedUserAndListingAsync();

        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Cart-Session");
        Client.DefaultRequestHeaders.Add("X-Cart-Session", $"session-{Guid.NewGuid():N}");

        var dto = new AddCartItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 2
        };

        var response = await Client.PostAsJsonAsync("/api/v1/carts/items", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Clear_AnonymousWithSession_ReturnsNoContent()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove("X-Cart-Session");
        Client.DefaultRequestHeaders.Add("X-Cart-Session", $"session-{Guid.NewGuid():N}");

        var response = await Client.DeleteAsync("/api/v1/carts/current");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Checkout_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var dto = new CheckoutDto
        {
            ShippingFullName = "Anonymous",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000000"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/carts/checkout", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
