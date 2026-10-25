using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 10 (R49) — ثبت موجودی توسط فروشنده.
///
/// تا پیش از این هیچ راهی برای نوشتن موجودی وجود نداشت: هر Listing با
/// موجودی صفر متولد می‌شد و checkout همیشه INSUFFICIENT_STOCK می‌داد.
/// این تست‌ها همان بن‌بست و مرزهای مالکیت را قفل می‌کنند.
/// </summary>
public class Phase10InventoryTests : BaseIntegrationTest
{
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase10InventoryTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Customer")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    // ==================== مسیر خوشحال ====================

    [Fact]
    public async Task AddStock_ByOwningSeller_MakesListingBuyable()
    {
        var (seller, listingId) = await CreateReadyListingAsync();

        SetCurrentUser(seller.UserId);

        var before = await InventoryService.GetAvailableAsync(listingId);
        before.Should().Be(0, "a fresh listing has no stock");

        var result = await InventoryService.AddStockAsync(listingId, null, 25m);

        result.AvailableQuantity.Should().Be(25m);
        result.ListingId.Should().Be(listingId);
        result.WarehouseId.Should().BeGreaterThan(0);

        (await InventoryService.GetAvailableAsync(listingId)).Should().Be(25m);
    }

    [Fact]
    public async Task AddStock_Twice_AccumulatesInsteadOfOverwriting()
    {
        var (seller, listingId) = await CreateReadyListingAsync();
        SetCurrentUser(seller.UserId);

        await InventoryService.AddStockAsync(listingId, null, 10m);
        var second = await InventoryService.AddStockAsync(listingId, null, 5m);

        second.AvailableQuantity.Should().Be(15m);

        var rows = await DbContext.Inventory.AsNoTracking()
            .Where(i => i.ListingId == listingId).ToListAsync();

        rows.Should().HaveCount(1, "the same warehouse must not get a second row");
        rows[0].Quantity.Should().Be(15m);
    }

    [Fact]
    public async Task AddStock_NoWarehouseGiven_ProvisionsPlatformWarehouseOnce()
    {
        var (seller, listingId) = await CreateReadyListingAsync();
        SetCurrentUser(seller.UserId);

        await InventoryService.AddStockAsync(listingId, null, 5m);
        await InventoryService.AddStockAsync(listingId, null, 5m);

        var platformWarehouses = await DbContext.Warehouse.AsNoTracking()
            .Where(w => w.SellerId == null).ToListAsync();

        platformWarehouses.Should().HaveCount(1, "the default warehouse must be reused, not duplicated");
    }

    [Fact]
    public async Task AddStock_ByAdmin_IsAllowedOnAnyListing()
    {
        var (_, listingId) = await CreateReadyListingAsync();

        SetCurrentUser(1, "Admin");

        var result = await InventoryService.AddStockAsync(listingId, null, 3m);
        result.AvailableQuantity.Should().Be(3m);
    }

    // ==================== مالکیت ====================

    [Fact]
    public async Task AddStock_ByAnotherSeller_IsForbidden()
    {
        var (_, listingId) = await CreateReadyListingAsync();
        var intruder = await CreateSellerAsync();

        SetCurrentUser(intruder.UserId);

        var act = async () => await InventoryService.AddStockAsync(listingId, null, 100m);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.Code == "LISTING_NOT_OWNED");

        (await InventoryService.GetAvailableAsync(listingId)).Should().Be(0,
            "a rejected attempt must not leave stock behind");
    }

    [Fact]
    public async Task AddStock_BySuspendedSeller_IsForbidden()
    {
        // Building a listing for a Pending seller is impossible by design
        // (CreateListingAsync refuses it), so the realistic scenario is a seller
        // who was suspended *after* their listing went live. Ownership passes,
        // the approval gate is what rejects.
        var (seller, listingId) = await CreateReadyListingAsync();

        seller.Status = SellerStatus.Suspended;
        await DbContext.SaveChangesAsync();

        SetCurrentUser(seller.UserId);

        var act = async () => await InventoryService.AddStockAsync(listingId, null, 5m);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.Code == "SELLER_NOT_APPROVED");
    }

    [Fact]
    public async Task AddStock_ByPlainCustomer_IsForbidden()
    {
        var (_, listingId) = await CreateReadyListingAsync();
        var (customer, _) = await CreateUserWithProfileAsync();

        SetCurrentUser(customer.UserId, "Customer");

        var act = async () => await InventoryService.AddStockAsync(listingId, null, 5m);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ==================== اعتبارسنجی ====================

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddStock_NonPositiveQuantity_IsRejected(decimal quantity)
    {
        var (seller, listingId) = await CreateReadyListingAsync();
        SetCurrentUser(seller.UserId);

        var act = async () => await InventoryService.AddStockAsync(listingId, null, quantity);

        await act.Should().ThrowAsync<BusinessException>()
            .Where(e => e.Code == "INVALID_QUANTITY");
    }

    [Fact]
    public async Task AddStock_UnknownListing_ThrowsNotFound()
    {
        SetCurrentUser(1, "Admin");

        var act = async () => await InventoryService.AddStockAsync(999_999, null, 1m);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddStock_UnknownWarehouse_ThrowsNotFound()
    {
        var (seller, listingId) = await CreateReadyListingAsync();
        SetCurrentUser(seller.UserId);

        var act = async () => await InventoryService.AddStockAsync(listingId, 999_999, 1m);

        await act.Should().ThrowAsync<NotFoundException>()
            .Where(e => e.Code == "WAREHOUSE_NOT_FOUND");
    }

    // ==================== endpoint ====================

    [Fact]
    public async Task InventoryEndpoint_AnonymousUser_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/listings/1/inventory", new { quantity = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==================== ابزار ====================

    private async Task<(Seller seller, int listingId)> CreateReadyListingAsync()
    {
        var seller = await CreateSellerAsync();

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P10-{Guid.NewGuid():N}",
            Price = 100m,
            SubcategoryId = 1
        });

        var variant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 100m,
            Status = SellerProductStatus.Active
        });

        return (seller, listing.ListingId);
    }

    private async Task<Seller> CreateSellerAsync(SellerStatus status = SellerStatus.Approved)
    {
        var (user, _) = await CreateUserWithProfileAsync();

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"P10Shop-{Guid.NewGuid():N}",
            Status = status,
            IsActive = true
        };

        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        return seller;
    }

    private async Task<(User user, Customer customer)> CreateUserWithProfileAsync()
    {
        var user = new User
        {
            FirstName = "P10",
            LastName = "User",
            PhoneNumber = $"0914{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }
}