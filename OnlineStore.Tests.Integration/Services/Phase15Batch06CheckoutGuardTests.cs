using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 6 — گاردهای checkout (F-11، F-15).
///
///  • F-11: روش ارسال وقتی دست‌کم یک روش فعال وجود دارد اجباری است؛ همهٔ مبالغ
///    (اقلام، ارسال، تخفیف، جمع) روی سرور محاسبه می‌شوند و فیلدهای اضافهٔ کلاینت بی‌اثرند.
///  • F-15: محصول یا واریانت غیرفعال (حذف‌شده) نه در فهرست عمومی دیده می‌شود
///    و نه قابل افزودن به سبد یا نهایی‌کردن است؛ حذف محصول پیشنهادهای فروشش را غیرفعال می‌کند.
///
/// گاردها از میان‌افزار موجود عبور می‌کنند: 422 برای ورودی نامعتبر، 409 برای تعارض وضعیت.
/// </summary>
public class Phase15Batch06CheckoutGuardTests : ControllerIntegrationTestBase
{
    private const decimal ItemPrice = 250m;
    private const decimal ShippingBaseCost = 45m;

    private AppDbContext DbContext => GetService<AppDbContext>();
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private IOrderService OrderService => GetService<IOrderService>();

    public Phase15Batch06CheckoutGuardTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== F-11: روش ارسال ====================

    [Fact]
    public async Task Checkout_WithoutShippingMethod_WhenActiveMethodExists_Returns422_AndCreatesNoOrder()
    {
        await CreateShippingMethodAsync(isActive: true);
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 2);

        var response = await CheckoutAsync(token, new { ShippingFullName = "B6 Buyer", ShippingAddress = "Tehran", ShippingPhoneNumber = "09120000000" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "SHIPPING_METHOD_REQUIRED");
        (await OrderCountAsync(userId)).Should().Be(0);
        (await CartItemCountAsync(userId)).Should().Be(1, "a rejected checkout must leave the cart untouched");
    }

    [Fact]
    public async Task Checkout_ClientCannotLowerTotal_ByOmittingShippingOrSendingItsOwnAmounts()
    {
        var method = await CreateShippingMethodAsync(isActive: true);
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 2);

        // مبالغ جعلی کلاینت، بدون روش ارسال: باید رد شود، نه با ارسال رایگان پذیرفته شود.
        var tamperedWithoutShipping = new
        {
            ShippingFullName = "B6 Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000",
            TotalAmount = 1m,
            ShippingCost = 0m,
            DiscountAmount = 99999m,
            UnitPrice = 1m
        };

        var rejected = await CheckoutAsync(token, tamperedWithoutShipping);
        await AssertProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, "SHIPPING_METHOD_REQUIRED");
        (await OrderCountAsync(userId)).Should().Be(0);

        // همان مبالغ جعلی با روش ارسال: پذیرفته می‌شود اما همهٔ مبالغ از سرور است.
        var accepted = await CheckoutAsync(token, new
        {
            tamperedWithoutShipping.ShippingFullName,
            tamperedWithoutShipping.ShippingAddress,
            tamperedWithoutShipping.ShippingPhoneNumber,
            tamperedWithoutShipping.TotalAmount,
            tamperedWithoutShipping.ShippingCost,
            tamperedWithoutShipping.DiscountAmount,
            tamperedWithoutShipping.UnitPrice,
            ShippingMethodId = method.ShippingMethodId
        });

        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        var order = await DbContext.Order.AsNoTracking().Include(o => o.OrderItems)
            .SingleAsync(o => o.Customer.UserId == userId);

        order.OrderItems.Should().ContainSingle().Which.UnitPrice.Should().Be(ItemPrice);
        order.ShippingCost.Should().Be(ShippingBaseCost);
        order.DiscountAmount.Should().Be(0m);
        order.ShippingMethodId.Should().Be(method.ShippingMethodId);
        order.TotalAmount.Should().Be(2 * ItemPrice + ShippingBaseCost);
    }

    [Fact]
    public async Task Checkout_WithInactiveShippingMethod_Returns422InvalidShippingMethod()
    {
        await CreateShippingMethodAsync(isActive: true);
        var inactive = await CreateShippingMethodAsync(isActive: false);
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 1);

        var response = await CheckoutAsync(token, new
        {
            ShippingFullName = "B6 Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000",
            ShippingMethodId = inactive.ShippingMethodId
        });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "INVALID_SHIPPING_METHOD");
        (await OrderCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task Checkout_WhenNoActiveShippingMethodIsConfigured_AllowsCheckoutWithZeroShipping()
    {
        // قاعدهٔ طرح: «اجباری وقتی دست‌کم یک روش فعال وجود دارد». روش غیرفعال حساب نمی‌شود.
        await CreateShippingMethodAsync(isActive: false);
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 1);

        var response = await CheckoutAsync(token, new { ShippingFullName = "B6 Buyer", ShippingAddress = "Tehran", ShippingPhoneNumber = "09120000000" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.Customer.UserId == userId);
        order.ShippingCost.Should().Be(0m);
        order.TotalAmount.Should().Be(ItemPrice);
    }

    // ==================== F-15: محصول/واریانت غیرفعال ====================

    [Fact]
    public async Task AddToCart_DeletedProduct_Returns409ListingNotActive()
    {
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await DeactivateProductDirectlyAsync(seeded.ProductId);

        var response = await PostAddToCartAsync(token, seeded.ListingId, 1);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "LISTING_NOT_AVAILABLE");
        (await CartItemCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task AddToCart_InactiveVariant_Returns409ListingNotActive()
    {
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await DeactivateVariantDirectlyAsync(seeded.VariantId);

        var response = await PostAddToCartAsync(token, seeded.ListingId, 1);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "LISTING_NOT_AVAILABLE");
        (await CartItemCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task Checkout_ProductDeactivatedAfterAddingToCart_Returns409_AndCreatesNoOrder()
    {
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 1);

        // فقط محصول غیرفعال می‌شود؛ Listing همچنان Active است تا گارد محصول جداگانه سنجیده شود.
        await DeactivateProductDirectlyAsync(seeded.ProductId);

        var response = await CheckoutAsync(token, new { ShippingFullName = "B6 Buyer", ShippingAddress = "Tehran", ShippingPhoneNumber = "09120000000" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "LISTING_NOT_AVAILABLE");
        (await OrderCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task Checkout_VariantDeactivatedAfterAddingToCart_Returns409_AndCreatesNoOrder()
    {
        var (token, userId) = await RegisterCustomerAsync();
        var seeded = await SeedListingAsync();
        await AddToCartAsync(token, seeded.ListingId, 1);

        await DeactivateVariantDirectlyAsync(seeded.VariantId);

        var response = await CheckoutAsync(token, new { ShippingFullName = "B6 Buyer", ShippingAddress = "Tehran", ShippingPhoneNumber = "09120000000" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "LISTING_NOT_AVAILABLE");
        (await OrderCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task AddItemToOrder_InactiveProduct_Returns409ListingNotAvailable()
    {
        var seeded = await SeedListingAsync();
        await DeactivateProductDirectlyAsync(seeded.ProductId);

        var (buyerUserId, customerId) = await CreateCustomerDirectlyAsync();
        SetCurrentUser(buyerUserId, "Customer");
        var order = await OrderService.CreateEmptyOrderAsync(customerId, "B6 Buyer", "Tehran", "09120000000");

        var act = () => OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = seeded.ListingId, Quantity = 1 });

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("LISTING_NOT_AVAILABLE");
    }

    [Fact]
    public async Task DeleteProduct_DeactivatesItsListings()
    {
        var seeded = await SeedListingAsync();
        SetCurrentUser(1, "Admin");

        (await ProductService.DeleteAsync(seeded.ProductId)).Should().BeTrue();

        var status = await DbContext.SellerProduct.AsNoTracking()
            .Where(l => l.ListingId == seeded.ListingId).Select(l => l.Status).SingleAsync();
        status.Should().Be(SellerProductStatus.Inactive);
    }

    [Fact]
    public async Task GetListingsByVariant_ProductDeactivated_ReturnsNothing()
    {
        var seeded = await SeedListingAsync();
        (await SellerService.GetListingsByVariantIdAsync(seeded.VariantId)).Should().ContainSingle();

        await DeactivateProductDirectlyAsync(seeded.ProductId);

        (await SellerService.GetListingsByVariantIdAsync(seeded.VariantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetProducts_Public_ExcludesDeletedProduct()
    {
        var seeded = await SeedListingAsync();
        await DeactivateProductDirectlyAsync(seeded.ProductId);

        Client.DefaultRequestHeaders.Authorization = null;
        var names = await ListProductNamesAsync(seeded.ProductName, includeInactive: false);

        names.Should().NotContain(seeded.ProductName);
    }

    [Fact]
    public async Task GetProducts_IncludeInactiveWithoutCatalogPermission_StillExcludesDeletedProduct()
    {
        var seeded = await SeedListingAsync();
        await DeactivateProductDirectlyAsync(seeded.ProductId);
        var (token, _) = await RegisterCustomerAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var names = await ListProductNamesAsync(seeded.ProductName, includeInactive: true);

        names.Should().NotContain(seeded.ProductName);
    }

    [Fact]
    public async Task GetProducts_IncludeInactiveWithCatalogPermission_ReturnsDeletedProduct()
    {
        var seeded = await SeedListingAsync();
        await DeactivateProductDirectlyAsync(seeded.ProductId);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());
        var withFlag = await ListProductNamesAsync(seeded.ProductName, includeInactive: true);
        var withoutFlag = await ListProductNamesAsync(seeded.ProductName, includeInactive: false);

        withFlag.Should().Contain(seeded.ProductName);
        withoutFlag.Should().NotContain(seeded.ProductName, "inactive products are hidden by default, even for admins");
    }

    // ==================== Helpers ====================

    private sealed record SeededListing(int ListingId, int VariantId, int ProductId, string ProductName);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private async Task<(string Token, int UserId)> RegisterCustomerAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "B6",
            LastName = "Buyer",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var userId = await DbContext.User.AsNoTracking().Where(u => u.PhoneNumber == phone).Select(u => u.UserId).SingleAsync();
        return (result!.AccessToken, userId);
    }

    private async Task<(int UserId, int CustomerId)> CreateCustomerDirectlyAsync()
    {
        var user = new User
        {
            FirstName = "B6",
            LastName = "Buyer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();
        return (user.UserId, customer.CustomerId);
    }

    private async Task<ShippingMethod> CreateShippingMethodAsync(bool isActive)
    {
        var method = new ShippingMethod
        {
            Name = $"Post-{Guid.NewGuid():N}",
            Slug = $"post-{Guid.NewGuid():N}",
            BaseCost = ShippingBaseCost,
            IsActive = isActive
        };
        DbContext.ShippingMethod.Add(method);
        await DbContext.SaveChangesAsync();
        return method;
    }

    private async Task<SeededListing> SeedListingAsync()
    {
        var (sellerUserId, _) = await CreateCustomerDirectlyAsync();

        var seller = new Seller
        {
            UserId = sellerUserId,
            StoreName = $"B6Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B6-{Guid.NewGuid():N}",
            Price = ItemPrice,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", IsActive = true };
        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = ItemPrice,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B6",
            RecipientLastName = "Shop",
            UserId = sellerUserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 50m));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        return new SeededListing(listing.ListingId, variant.VariantId, product.ProductId, product.Name);
    }

    /// <summary>غیرفعال‌سازی مستقیم در دیتابیس (بدون DeleteAsync) تا Listing همچنان Active بماند.</summary>
    private async Task DeactivateProductDirectlyAsync(int productId)
    {
        await DbContext.Product.Where(p => p.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        DbContext.ChangeTracker.Clear();
    }

    private async Task DeactivateVariantDirectlyAsync(int variantId)
    {
        await DbContext.ProductVariant.Where(v => v.VariantId == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsActive, false));
        DbContext.ChangeTracker.Clear();
    }

    private Task<HttpResponseMessage> PostAddToCartAsync(string token, int listingId, decimal quantity)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = quantity });
    }

    private async Task AddToCartAsync(string token, int listingId, decimal quantity)
    {
        var response = await PostAddToCartAsync(token, listingId, quantity);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> CheckoutAsync(string token, object body)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.PostAsJsonAsync("/api/v1/carts/checkout", body);
    }

    private Task<int> OrderCountAsync(int userId)
        => DbContext.Order.AsNoTracking().CountAsync(o => o.Customer.UserId == userId);

    private Task<int> CartItemCountAsync(int userId)
        => DbContext.CartItem.AsNoTracking().CountAsync(i => i.Cart.UserId == userId);

    private async Task<List<string>> ListProductNamesAsync(string name, bool includeInactive)
    {
        var filter = Uri.EscapeDataString($"Name contains '{name}'");
        var url = $"/api/v1/products?filter={filter}" + (includeInactive ? "&includeInactive=true" : string.Empty);

        var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()!)
            .ToList();
    }
}
