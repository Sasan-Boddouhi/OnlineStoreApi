using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class OrderServiceIntegrationTests : BaseIntegrationTest
{
    private IOrderService OrderService => GetService<IOrderService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public OrderServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    /// <summary>Phase 6: هر Listing به یک واریانت وصل است، پس تست‌ها یک واریانت پیش‌فرض می‌سازند.</summary>
    private async Task<ProductVariant> CreateVariantAsync(int productId, string? name = null)
    {
        var variant = new ProductVariant
        {
            ProductId = productId,
            Name = name ?? "Default",
            Sku = $"SKU-{Guid.NewGuid():N}"
        };

        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();
        return variant;
    }

    /// <summary>
    /// مالکیت سفارش اکنون با کاربر جاری سنجیده می‌شود؛ پس باید کاربر جاری را با کاربر مالک سفارش هم‌راستا کرد.
    /// </summary>
    private void SetCurrentUser(int userId)
        => ((FakeCurrentUserService)GetService<ICurrentUserService>()).CurrentUserId = userId;

    private async Task<(Customer customer, User user)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Cust",
            LastName = "Omer",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (customer, user);
    }

    /// <summary>
    /// Phase 2: موجودی دیگر به محصول تعلق ندارد، به Listing (پیشنهاد فروش) تعلق دارد.
    /// پس زنجیرهٔ Seller -> SellerProduct -> Warehouse -> Inventory ساخته می‌شود.
    /// </summary>
    private async Task<SellerProduct> CreateListingAsync(
        int productId,
        int ownerUserId,
        decimal quantity,
        decimal price = 50,
        SellerProductStatus status = SellerProductStatus.Active)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"Store-{Guid.NewGuid():N}",
            Slug = $"store-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var variant = await CreateVariantAsync(productId);

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = status
        };
        DbContext.SellerProduct.Add(listing);
        await DbContext.SaveChangesAsync();

        await CreateInventoryAsync(listing.ListingId, seller.SellerId, ownerUserId, quantity);

        return listing;
    }

    /// <summary>
    /// زنجیره Province -> City -> Address -> Warehouse -> Inventory را می‌سازد،
    /// چون موجودی بدون انبار قابل ثبت نیست.
    /// </summary>
    private async Task CreateInventoryAsync(int listingId, int sellerId, int ownerUserId, decimal quantity)
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
            UserId = ownerUserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"W-{Guid.NewGuid():N}",
            AddressId = address.AddressId,
            SellerId = sellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listingId, warehouse.WarehouseId, quantity));
        await DbContext.SaveChangesAsync();
    }

    private static Product NewProduct(string name, decimal price = 50)
        => new() { Name = name, Price = price, SubcategoryId = 1 };

    [Fact]
    public async Task CreateEmptyOrderAsync_ValidCustomer_CreatesOrder()
    {
        var (customer, _) = await CreateCustomerAsync();
        var result = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Ali", "Addr", "09121111111");
        result.Should().NotBeNull();
        result.OrderId.Should().BeGreaterThan(0);

        var fromDb = await DbContext.Order.FindAsync(result.OrderId);
        fromDb.Should().NotBeNull();
        fromDb!.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task AddItemToOrderAsync_ValidListing_AddsItemAndUsesListingPrice()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var product = NewProduct("Test Product");
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        // Product.Price = 50 است ولی قیمت Listing عمداً 70 گذاشته می‌شود تا D2 سنجیده شود.
        var listing = await CreateListingAsync(product.ProductId, user.UserId, 10, price: 70);

        var itemDto = new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 2, Description = "Item" };
        var updated = await OrderService.AddItemToOrderAsync(orderDto.OrderId, itemDto);

        // D2: قیمت مرجع سفارش، Listing.Price است نه Product.Price.
        updated.TotalAmount.Should().Be(140);

        var items = await DbContext.OrderItem.Where(i => i.OrderId == orderDto.OrderId).ToListAsync();
        items.Should().HaveCount(1);
        items[0].ListingId.Should().Be(listing.ListingId);
        items[0].ProductId.Should().Be(product.ProductId);
        items[0].UnitPrice.Should().Be(70);

        var inventory = await DbContext.Inventory.FirstAsync(i => i.ListingId == listing.ListingId);
        inventory.Quantity.Should().Be(8);
    }

    [Fact]
    public async Task AddItemToOrderAsync_TwoListingsOfSameProduct_OnlyChosenListingIsDecreased()
    {
        var (customer, user) = await CreateCustomerAsync();
        var (_, otherUser) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var product = NewProduct("Shared Product");
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var chosen = await CreateListingAsync(product.ProductId, user.UserId, 10);
        var untouched = await CreateListingAsync(product.ProductId, otherUser.UserId, 10);

        await OrderService.AddItemToOrderAsync(
            orderDto.OrderId,
            new AddOrderItemDto { ListingId = chosen.ListingId, Quantity = 3 });

        var chosenInventory = await DbContext.Inventory.FirstAsync(i => i.ListingId == chosen.ListingId);
        var untouchedInventory = await DbContext.Inventory.FirstAsync(i => i.ListingId == untouched.ListingId);

        chosenInventory.Quantity.Should().Be(7);
        untouchedInventory.Quantity.Should().Be(10);
    }

    [Fact]
    public async Task AddItemToOrderAsync_InsufficientStock_ThrowsBusinessException()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var product = NewProduct("Scarce Product");
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var listing = await CreateListingAsync(product.ProductId, user.UserId, 1);

        var itemDto = new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 5 };

        await FluentActions
            .Awaiting(() => OrderService.AddItemToOrderAsync(orderDto.OrderId, itemDto))
            .Should().ThrowAsync<Application.Exceptions.BusinessException>();
    }

    [Fact]
    public async Task AddItemToOrderAsync_UnknownListing_ThrowsListingNotFound()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var ex = await FluentActions
            .Awaiting(() => OrderService.AddItemToOrderAsync(
                orderDto.OrderId,
                new AddOrderItemDto { ListingId = 999_999, Quantity = 1 }))
            .Should().ThrowAsync<Application.Exceptions.BusinessException>();

        ex.And.Code.Should().Be("LISTING_NOT_FOUND");
    }

    [Fact]
    public async Task AddItemToOrderAsync_InactiveListing_ThrowsListingNotAvailable()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var product = NewProduct("Inactive Product");
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var listing = await CreateListingAsync(
            product.ProductId, user.UserId, 10, status: SellerProductStatus.Inactive);

        var ex = await FluentActions
            .Awaiting(() => OrderService.AddItemToOrderAsync(
                orderDto.OrderId,
                new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 }))
            .Should().ThrowAsync<Application.Exceptions.BusinessException>();

        ex.And.Code.Should().Be("LISTING_NOT_AVAILABLE");
    }

    [Fact]
    public async Task AddItemToOrderAsync_NonOwnedOrder_ThrowsNotFoundException()
    {
        var (customer, _) = await CreateCustomerAsync();
        var (_, otherUser) = await CreateCustomerAsync();

        SetCurrentUser(otherUser.UserId);

        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var product = NewProduct("Foreign Product");
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var listing = await CreateListingAsync(product.ProductId, otherUser.UserId, 10);

        var itemDto = new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 };

        await FluentActions
            .Awaiting(() => OrderService.AddItemToOrderAsync(orderDto.OrderId, itemDto))
            .Should().ThrowAsync<Application.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task ConfirmOrderAsync_ChangesStatus()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var confirmed = await OrderService.ConfirmOrderAsync(order.OrderId);
        confirmed.Status.Should().NotBe("Pending");

        var fromDb = await DbContext.Order.FindAsync(order.OrderId);
        fromDb!.Status.Should().NotBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task CancelOrderAsync_ChangesStatus()
    {
        var (customer, user) = await CreateCustomerAsync();
        SetCurrentUser(user.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var cancelled = await OrderService.CancelOrderAsync(order.OrderId);
        cancelled.Status.Should().Be("Cancelled");

        var fromDb = await DbContext.Order.FindAsync(order.OrderId);
        fromDb!.Status.Should().Be(OrderStatus.Cancelled);
    }
}