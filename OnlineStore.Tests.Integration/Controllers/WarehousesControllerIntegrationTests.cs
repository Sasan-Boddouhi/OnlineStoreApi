using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Warehouse;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class WarehousesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public WarehousesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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

    private async Task<Address> CreateAddressAsync(int userId)
    {
        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "10",
            Unit = "2",
            PostalCode = "1234567890",
            RecipientFirstName = "Rec",
            RecipientLastName = "Ipient",
            UserId = userId
        };
        db.Address.Add(address);
        await db.SaveChangesAsync();

        return address;
    }

    [Fact]
    public async Task Create_Seller_AutoAssignsSellerId()
    {
        var (token, userId, seller) = await CreateSellerAndTokenAsync();
        var address = await CreateAddressAsync(userId);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = new CreateWarehouseDto
        {
            Name = "Seller Warehouse " + Guid.NewGuid().ToString("N")[..8],
            AddressId = address.AddressId,
            SellerId = null // Auto-assigned by controller/service
        };

        var response = await Client.PostAsJsonAsync("/api/v1/warehouses", createDto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<WarehouseDto>();
        created.Should().NotBeNull();
        created!.SellerId.Should().Be(seller.SellerId);
    }

    [Fact]
    public async Task GetAll_Seller_ReturnsOnlyOwnWarehouses()
    {
        var (tokenA, userIdA, sellerA) = await CreateSellerAndTokenAsync();
        var (tokenB, userIdB, sellerB) = await CreateSellerAndTokenAsync();

        var addressA = await CreateAddressAsync(userIdA);
        var addressB = await CreateAddressAsync(userIdB);

        var db = GetService<AppDbContext>();
        var whA = new Warehouse { Name = "WhA-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressA.AddressId, SellerId = sellerA.SellerId };
        var whB = new Warehouse { Name = "WhB-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressB.AddressId, SellerId = sellerB.SellerId };
        db.Warehouse.AddRange(whA, whB);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var response = await Client.GetAsync("/api/v1/warehouses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<WarehouseDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(w => w.WarehouseId == whA.WarehouseId);
        list.Should().NotContain(w => w.WarehouseId == whB.WarehouseId);
    }

    [Fact]
    public async Task GetAll_Admin_ReturnsAllWarehouses()
    {
        var (_, userIdA, sellerA) = await CreateSellerAndTokenAsync();
        var (_, userIdB, sellerB) = await CreateSellerAndTokenAsync();

        var addressA = await CreateAddressAsync(userIdA);
        var addressB = await CreateAddressAsync(userIdB);

        var db = GetService<AppDbContext>();
        var whA = new Warehouse { Name = "WhAdminA-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressA.AddressId, SellerId = sellerA.SellerId };
        var whB = new Warehouse { Name = "WhAdminB-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressB.AddressId, SellerId = sellerB.SellerId };
        db.Warehouse.AddRange(whA, whB);
        await db.SaveChangesAsync();

        var adminToken = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.GetAsync("/api/v1/warehouses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<WarehouseDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(w => w.WarehouseId == whA.WarehouseId);
        list.Should().Contain(w => w.WarehouseId == whB.WarehouseId);
    }

    [Fact]
    public async Task GetById_Seller_OwnWarehouse_ReturnsOk()
    {
        var (tokenA, userIdA, sellerA) = await CreateSellerAndTokenAsync();
        var addressA = await CreateAddressAsync(userIdA);

        var db = GetService<AppDbContext>();
        var whA = new Warehouse { Name = "WhOwn-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressA.AddressId, SellerId = sellerA.SellerId };
        db.Warehouse.Add(whA);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var response = await Client.GetAsync($"/api/v1/warehouses/{whA.WarehouseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<WarehouseDto>();
        result.Should().NotBeNull();
        result!.WarehouseId.Should().Be(whA.WarehouseId);
    }

    [Fact]
    public async Task GetById_Seller_OtherWarehouse_ReturnsForbidden()
    {
        var (tokenA, _, _) = await CreateSellerAndTokenAsync();
        var (_, userIdB, sellerB) = await CreateSellerAndTokenAsync();
        var addressB = await CreateAddressAsync(userIdB);

        var db = GetService<AppDbContext>();
        var whB = new Warehouse { Name = "WhOther-" + Guid.NewGuid().ToString("N")[..8], AddressId = addressB.AddressId, SellerId = sellerB.SellerId };
        db.Warehouse.Add(whB);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var response = await Client.GetAsync($"/api/v1/warehouses/{whB.WarehouseId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
