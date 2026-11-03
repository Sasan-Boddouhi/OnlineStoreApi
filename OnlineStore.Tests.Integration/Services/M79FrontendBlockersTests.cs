using System.Net;
using System.Net.Http.Json;
using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Address;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Services;

public class M79FrontendBlockersTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private ICartService CartService => GetService<ICartService>();
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private IAddressService AddressService => GetService<IAddressService>();
    private IProductCategoryService CategoryService => GetService<IProductCategoryService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public M79FrontendBlockersTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId)
        => ((FakeCurrentUserService)GetService<ICurrentUserService>()).CurrentUserId = userId;

    private async Task<int> CreateCityAsync()
    {
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        return city.CityId;
    }

    private async Task<(User User, Customer Customer)> CreateBuyerAsync()
    {
        var user = new User
        {
            FirstName = "Buyer",
            LastName = "Test",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer
        {
            UserId = user.UserId,
            User = user
        };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    private async Task<(Seller Seller, Product Product, ProductVariant Variant, SellerProduct Listing)> SetupListingWithStockAsync(decimal price = 250m, decimal stock = 10m)
    {
        var user = new User
        {
            FirstName = "Seller",
            LastName = "Owner",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"Store-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var product = new Product
        {
            Name = $"Test Phone-{Guid.NewGuid():N}",
            Price = price,
            SubcategoryId = 1,
            ImageUrl = "https://example.com/phone.jpg"
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.ProductId,
            Name = "256GB Black",
            Sku = $"SKU-{Guid.NewGuid():N}"
        };
        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);
        await DbContext.SaveChangesAsync();

        var cityId = await CreateCityAsync();

        var address = new Address
        {
            CityId = cityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "12345",
            RecipientFirstName = "W",
            RecipientLastName = "H",
            UserId = user.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"WH-{Guid.NewGuid():N}",
            AddressId = address.AddressId,
            SellerId = seller.SellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        var inventory = new Inventory(listing.ListingId, warehouse.WarehouseId, stock);
        DbContext.Inventory.Add(inventory);
        await DbContext.SaveChangesAsync();

        return (seller, product, variant, listing);
    }

    // ==================== 1. AbandonedOrderCleanupService ====================
    [Fact]
    public async Task AbandonedOrderCleanupService_CancelsOrdersOlderThan60Min_AndReleasesStock()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(stock: 10m);
        var (buyerUser, buyerCustomer) = await CreateBuyerAsync();
        SetCurrentUser(buyerUser.UserId);

        // Add to cart and checkout 4 items -> available becomes 6
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 4m });
        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer One",
            ShippingAddress = "Tehran, Valiasr St",
            ShippingPhoneNumber = "09123456789"
        });

        var availableAfterCheckout = await InventoryService.GetAvailableAsync(listing.ListingId);
        availableAfterCheckout.Should().Be(6m);

        // Backdate the order to 75 minutes ago
        var orderEntity = await DbContext.Order.FindAsync(order.OrderId);
        orderEntity!.OrderDate = DateTime.UtcNow.AddMinutes(-75);
        await DbContext.SaveChangesAsync();

        // Run the cleanup service
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var cleanupService = new AbandonedOrderCleanupService(scopeFactory, NullLogger<AbandonedOrderCleanupService>.Instance);
        var cancelledCount = await cleanupService.RunCleanupAsync();

        cancelledCount.Should().BeGreaterThanOrEqualTo(1);

        // Verify order is cancelled and inventory is restored
        DbContext.ChangeTracker.Clear();
        var refreshedOrder = await DbContext.Order.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
        refreshedOrder!.Status.Should().Be(OrderStatus.Cancelled);

        var restoredAvailable = await InventoryService.GetAvailableAsync(listing.ListingId);
        restoredAvailable.Should().Be(10m);
    }

    // ==================== 2. StreetAddress in Address ====================
    [Fact]
    public async Task Address_WithStreetAddress_PersistsAndReturnsInDto()
    {
        var (user, _) = await CreateBuyerAsync();
        SetCurrentUser(user.UserId);
        var cityId = await CreateCityAsync();

        var createDto = new CreateAddressDto
        {
            CityId = cityId,
            StreetAddress = "خیابان آزادی، خیابان استاد معین",
            Plaque = "12",
            Unit = "4",
            PostalCode = "1345678901",
            RecipientFirstName = "Reza",
            RecipientLastName = "Ahmadi",
            IsDefault = true
        };

        var created = await AddressService.CreateAsync(user.UserId, createDto);
        created.Should().NotBeNull();
        created.StreetAddress.Should().Be("خیابان آزادی، خیابان استاد معین");

        var all = await AddressService.GetAllAsync(user.UserId);
        all.Should().ContainSingle(a => a.StreetAddress == "خیابان آزادی، خیابان استاد معین");
    }

    // ==================== 3. Variant Listings (PDP & Endpoint) ====================
    [Fact]
    public async Task VariantListings_PdpAndVariantEndpoint_ReturnActiveListings()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 320m, stock: 15m);

        // 3a. GET /api/v1/listings/variant/{variantId}
        var endpointResponse = await Client.GetAsync($"/api/v1/listings/variant/{variant.VariantId}");
        endpointResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listings = await endpointResponse.Content.ReadFromJsonAsync<List<VariantListingDto>>();
        listings.Should().NotBeNull();
        listings.Should().ContainSingle(l => l.ListingId == listing.ListingId && l.Price == 320m && l.AvailableQuantity == 15m);

        // 3b. ProductService.GetByIdAsync returns variant with Listings populated
        var pdpProduct = await ProductService.GetByIdAsync(product.ProductId);
        pdpProduct.Should().NotBeNull();
        var targetVariant = pdpProduct!.Variants.FirstOrDefault(v => v.VariantId == variant.VariantId);
        targetVariant.Should().NotBeNull();
        targetVariant!.Listings.Should().ContainSingle(l => l.ListingId == listing.ListingId && l.SellerStoreName == seller.StoreName);
    }

    // ==================== 4. Snapshots on OrderItem and CartItem ====================
    [Fact]
    public async Task Snapshots_OrderItemAndCartItem_ArePopulatedWithProductAndSellerData()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 500m, stock: 8m);
        var (buyerUser, _) = await CreateBuyerAsync();
        SetCurrentUser(buyerUser.UserId);

        // CartItem thumbnail
        var cart = await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 2m
        });

        cart.Items.Should().ContainSingle();
        var cartItem = cart.Items.First();
        cartItem.ProductImageUrl.Should().Be("https://example.com/phone.jpg");
        cartItem.SellerStoreName.Should().Be(seller.StoreName);
        cartItem.VariantName.Should().Be("256GB Black");

        // Order checkout snapshots
        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer Snapshots",
            ShippingAddress = "Tehran, Street 1",
            ShippingPhoneNumber = "09121112233"
        });

        var orderDetails = await DbContext.OrderItem.AsNoTracking().FirstOrDefaultAsync(oi => oi.OrderId == order.OrderId);
        orderDetails.Should().NotBeNull();
        orderDetails!.ProductName.Should().Be(product.Name);
        orderDetails.VariantName.Should().Be("256GB Black");
        orderDetails.ProductImageUrl.Should().Be("https://example.com/phone.jpg");
        orderDetails.SellerStoreName.Should().Be(seller.StoreName);
    }

    // ==================== 5. Category Tree ====================
    [Fact]
    public async Task CategoryTree_ReturnsNestedCategoriesAndSubcategories()
    {
        var response = await Client.GetAsync("/api/v1/categories/tree");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tree = await response.Content.ReadFromJsonAsync<List<CategoryTreeDto>>();
        tree.Should().NotBeNull();
        tree.Should().NotBeEmpty();

        var category = tree!.First();
        category.CategoryId.Should().BeGreaterThan(0);
        category.Name.Should().NotBeNullOrWhiteSpace();
        category.Subcategories.Should().NotBeNull();
    }
}
