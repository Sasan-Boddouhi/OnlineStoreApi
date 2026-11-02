using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Listing;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class InventoryControllerIntegrationTests : ControllerIntegrationTestBase
{
    public InventoryControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(SellerProduct Listing, Warehouse Warehouse)> SeedListingAndWarehouseAsync()
    {
        var db = GetService<AppDbContext>();

        var user = new User
        {
            FirstName = "Inv",
            LastName = "Seller",
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = "InvStore-" + Guid.NewGuid().ToString("N")[..8],
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "InvP-" + Guid.NewGuid().ToString("N")[..8],
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

        return (listing, warehouse);
    }

    [Fact]
    public async Task GetAvailable_AllowAnonymous_ReturnsOk()
    {
        var (listing, _) = await SeedListingAndWarehouseAsync();
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync($"/api/v1/inventory/available/{listing.ListingId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateInventory_Admin_ReturnsCreated()
    {
        var (listing, warehouse) = await SeedListingAndWarehouseAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateInventoryDto
        {
            ListingId = listing.ListingId,
            WarehouseId = warehouse.WarehouseId,
            InitialQuantity = 50
        };

        var response = await Client.PostAsJsonAsync("/api/v1/inventory", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateInventory_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var dto = new CreateInventoryDto
        {
            ListingId = 1,
            WarehouseId = 1,
            InitialQuantity = 10
        };

        var response = await Client.PostAsJsonAsync("/api/v1/inventory", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddStock_Admin_ReturnsOk()
    {
        var (listing, warehouse) = await SeedListingAndWarehouseAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new AddStockDto
        {
            WarehouseId = warehouse.WarehouseId,
            Quantity = 25
        };

        var response = await Client.PostAsJsonAsync($"/api/v1/inventory/add-stock/{listing.ListingId}", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
