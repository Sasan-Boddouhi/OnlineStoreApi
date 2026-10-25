using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 2 (§۵.۵) — مالکیت انبار.
/// قبل از این فاز، Warehouse هیچ مالکیتی نداشت و Name سراسری یکتا بود؛
/// یعنی «چند انبار داشتن» در عمل فقط انبارهای مشترک پلتفرم بود.
/// </summary>
public class WarehouseOwnershipTests : BaseIntegrationTest
{
    private AppDbContext DbContext => GetService<AppDbContext>();

    public WarehouseOwnershipTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<int> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "Wh",
            LastName = "Owner",
            PhoneNumber = $"0913{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();
        return user.UserId;
    }

    private async Task<Seller> CreateSellerAsync(int userId, string storeName)
    {
        var seller = new Seller
        {
            UserId = userId,
            StoreName = storeName,
            Status = SellerStatus.Approved
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task<Warehouse> CreateWarehouseAsync(string name, int? sellerId, int userId)
    {
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "Rec",
            RecipientLastName = "Ipient",
            UserId = userId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = name,
            AddressId = address.AddressId,
            SellerId = sellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        return warehouse;
    }

    [Fact]
    public async Task TwoSellers_CanEachOwnWarehouseWithSameName()
    {
        var userA = await CreateUserAsync();
        var userB = await CreateUserAsync();

        var sellerA = await CreateSellerAsync(userA, $"ShopA-{Guid.NewGuid():N}");
        var sellerB = await CreateSellerAsync(userB, $"ShopB-{Guid.NewGuid():N}");

        var whA = await CreateWarehouseAsync("Main", sellerA.SellerId, userA);
        var whB = await CreateWarehouseAsync("Main", sellerB.SellerId, userB);

        whA.SellerId.Should().Be(sellerA.SellerId);
        whB.SellerId.Should().Be(sellerB.SellerId);
        whA.Name.Should().Be(whB.Name);

        var count = await DbContext.Warehouse.CountAsync(w => w.Name == "Main");
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SameSeller_CannotHaveTwoWarehousesWithSameName()
    {
        var userId = await CreateUserAsync();
        var seller = await CreateSellerAsync(userId, $"ShopC-{Guid.NewGuid():N}");

        await CreateWarehouseAsync("Depot", seller.SellerId, userId);

        await FluentActions
            .Awaiting(() => CreateWarehouseAsync("Depot", seller.SellerId, userId))
            .Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PlatformWarehouses_WithSameName_AreRejected()
    {
        var userId = await CreateUserAsync();

        await CreateWarehouseAsync("Platform", null, userId);

        // انبار پلتفرم (SellerId = NULL) هنوز باید نام یکتا داشته باشد (ایندکس فیلترشده).
        await FluentActions
            .Awaiting(() => CreateWarehouseAsync("Platform", null, userId))
            .Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SellerOwnedWarehouse_DoesNotBlockPlatformWarehouseWithSameName()
    {
        var userId = await CreateUserAsync();
        var seller = await CreateSellerAsync(userId, $"ShopD-{Guid.NewGuid():N}");

        await CreateWarehouseAsync("Shared", seller.SellerId, userId);
        var platform = await CreateWarehouseAsync("Shared", null, userId);

        platform.SellerId.Should().BeNull();
    }
}