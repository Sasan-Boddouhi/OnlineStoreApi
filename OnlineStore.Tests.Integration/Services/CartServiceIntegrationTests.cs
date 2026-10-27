using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Order;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 4 — سبد خرید، ادغام مهمان و تبدیل سبد به سفارش.
/// </summary>
public class CartServiceIntegrationTests : BaseIntegrationTest
{
    private ICartService CartService => GetService<ICartService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public CartServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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

    private void SetCurrentUser(int userId)
        => ((FakeCurrentUserService)GetService<ICurrentUserService>()).CurrentUserId = userId;

    private static string NewSessionId() => $"sess-{Guid.NewGuid():N}";

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Cart",
            LastName = "Buyer",
            PhoneNumber = $"0913{new Random().Next(1000000, 9999999)}",
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

    private async Task<Product> CreateProductAsync(string name, decimal price)
    {
        var product = new Product { Name = name, Price = price, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        return product;
    }

    private async Task<(Seller seller, SellerProduct listing)> CreateListingAsync(
        int ownerUserId,
        int productId,
        decimal price,
        decimal quantity = 100,
        SellerProductStatus status = SellerProductStatus.Active)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"Cart-{Guid.NewGuid():N}",
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
            SellerId = seller.SellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, quantity));
        await DbContext.SaveChangesAsync();

        return (seller, listing);
    }

    // ==================== سبد ====================

    [Fact]
    public async Task AnonymousCart_IsCreatedAndScopedToSession()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Anon", 25);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 25);

        var cart = await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 2 });

        cart.IsAnonymous.Should().BeTrue();
        cart.Items.Should().HaveCount(1);
        cart.Items[0].Quantity.Should().Be(2);
        cart.Items[0].UnitPrice.Should().Be(25m);
        cart.TotalAmount.Should().Be(50m);

        // نشست دیگر سبد جداگانه می‌گیرد.
        var other = await CartService.GetCartAsync(null, NewSessionId());
        other.CartId.Should().NotBe(cart.CartId);
        other.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingSameListingTwice_MergesIntoOneRow()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Dup", 10);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 10);

        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });
        var cart = await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 3 });

        cart.Items.Should().HaveCount(1);
        cart.Items[0].Quantity.Should().Be(4);

        var rows = await DbContext.CartItem.CountAsync(i => i.CartId == cart.CartId);
        rows.Should().Be(1);
    }

    [Fact]
    public async Task AddItem_InactiveListing_IsRejected()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Inactive", 10);
        var (_, listing) = await CreateListingAsync(
            sellerUser.UserId, product.ProductId, 10, status: SellerProductStatus.Inactive);

        var act = () => CartService.AddItemAsync(
            null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("LISTING_NOT_ACTIVE");
    }

    [Fact]
    public async Task AddItem_MoreThanStock_IsRejected()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Scarce", 10);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 10, quantity: 2);

        var act = () => CartService.AddItemAsync(
            null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 3 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task UpdateQuantity_ToZero_RemovesItem()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Zero", 10);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 10);

        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 2 });

        var cart = await CartService.UpdateQuantityAsync(null, sessionId, listing.ListingId, 0);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveItem_UnknownListing_ReturnsNotFound()
    {
        var sessionId = NewSessionId();
        await CartService.GetCartAsync(null, sessionId);

        var act = () => CartService.RemoveItemAsync(null, sessionId, 999_999);

        var ex = await act.Should().ThrowAsync<NotFoundException>();
        ex.And.Code.Should().Be("CART_ITEM_NOT_FOUND");
    }

    [Fact]
    public async Task ClearCart_RemovesAllItemsButKeepsCart()
    {
        var sessionId = NewSessionId();
        // Seller.UserId یک‌به‌یک است، پس هر فروشنده کاربر خودش را دارد.
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        var productA = await CreateProductAsync("A", 10);
        var productB = await CreateProductAsync("B", 20);
        var (_, listingA) = await CreateListingAsync(sellerUserA.UserId, productA.ProductId, 10);
        var (_, listingB) = await CreateListingAsync(sellerUserB.UserId, productB.ProductId, 20);

        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        var cart = await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        await CartService.ClearCartAsync(null, sessionId);

        var after = await CartService.GetCartAsync(null, sessionId);
        after.CartId.Should().Be(cart.CartId);
        after.Items.Should().BeEmpty();
    }

    // ==================== ادغام مهمان ====================

    [Fact]
    public async Task MergeAnonymousCart_SumsQuantitiesAndDeletesGuestCart()
    {
        var sessionId = NewSessionId();
        var (buyer, _) = await CreateCustomerAsync();
        // Seller.UserId یک‌به‌یک است، پس هر فروشنده کاربر خودش را دارد.
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        var productA = await CreateProductAsync("A", 10);
        var productB = await CreateProductAsync("B", 20);
        var (_, listingA) = await CreateListingAsync(sellerUserA.UserId, productA.ProductId, 10);
        var (_, listingB) = await CreateListingAsync(sellerUserB.UserId, productB.ProductId, 20);

        // کاربر لاگین‌شده یک قلم دارد.
        var userCart = await CartService.AddItemAsync(
            buyer.UserId, null, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 1 });

        // مهمان هم همان قلم را دارد (+ یک قلم دیگر).
        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 2 });
        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listingB.ListingId, Quantity = 5 });

        var merged = await CartService.MergeAnonymousCartAsync(sessionId, buyer.UserId);

        merged.CartId.Should().Be(userCart.CartId);
        merged.Items.Should().HaveCount(2);
        merged.Items.Single(i => i.ListingId == listingA.ListingId).Quantity.Should().Be(3);
        merged.Items.Single(i => i.ListingId == listingB.ListingId).Quantity.Should().Be(5);

        var guestCart = await DbContext.Cart.FirstOrDefaultAsync(c => c.SessionId == sessionId);
        guestCart.Should().BeNull();
    }

    // ==================== Checkout ====================

    [Fact]
    public async Task Checkout_CreatesOrderWithSellerGroups_ReservesStock_AndClearsCart()
    {
        var (buyer, _) = await CreateCustomerAsync();
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        var productA = await CreateProductAsync("A", 100);
        var productB = await CreateProductAsync("B", 200);
        var (sellerA, listingA) = await CreateListingAsync(sellerUserA.UserId, productA.ProductId, 100, quantity: 10);
        var (sellerB, listingB) = await CreateListingAsync(sellerUserB.UserId, productB.ProductId, 200, quantity: 10);

        SetCurrentUser(buyer.UserId);

        var cart = await CartService.AddItemAsync(
            buyer.UserId, null, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 2 });
        cart = await CartService.AddItemAsync(
            buyer.UserId, null, new AddCartItemDto { ListingId = listingB.ListingId, Quantity = 1 });
        cart.TotalAmount.Should().Be(400m);

        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Cart Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        order.TotalAmount.Should().Be(400m);

        var groups = await DbContext.OrderSellerGroup.Where(g => g.OrderId == order.OrderId).ToListAsync();
        groups.Should().HaveCount(2);
        groups.Single(g => g.SellerId == sellerA.SellerId).SubTotal.Should().Be(200m);
        groups.Single(g => g.SellerId == sellerB.SellerId).SubTotal.Should().Be(200m);

        // موجودی رزرو شده است.
        var availableA = await GetService<IInventoryService>().GetAvailableAsync(listingA.ListingId);
        availableA.Should().Be(8m);

        // سبد خالی شده است.
        var after = await CartService.GetCartAsync(buyer.UserId, null);
        after.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Checkout_EmptyCart_IsRejected()
    {
        var (buyer, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);
        await CartService.GetCartAsync(buyer.UserId, null);

        var act = () => CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "X",
            ShippingAddress = "Y",
            ShippingPhoneNumber = "09120000000"
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("CART_EMPTY");
    }

    [Fact]
    public async Task Checkout_WithoutLogin_IsRejected()
    {
        var sessionId = NewSessionId();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Anon", 10);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 10);

        await CartService.AddItemAsync(null, sessionId, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var act = () => CartService.CheckoutAsync(null, sessionId, new CheckoutDto
        {
            ShippingFullName = "X",
            ShippingAddress = "Y",
            ShippingPhoneNumber = "09120000000"
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("CHECKOUT_REQUIRES_LOGIN");
    }

    [Fact]
    public async Task Checkout_OutOfStockItem_IsRejectedBeforeOrderIsCreated()
    {
        var (buyer, _) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Scarce", 10);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 10, quantity: 5);

        SetCurrentUser(buyer.UserId);

        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 5 });

        // موجودی را بیرون از سبد مصرف می‌کنیم تا سبد منقضی شود.
        await GetService<IInventoryService>().ReserveAsync(listing.ListingId, 5);

        var ordersBefore = await DbContext.Order.CountAsync();

        var act = () => CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "X",
            ShippingAddress = "Y",
            ShippingPhoneNumber = "09120000000"
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("INSUFFICIENT_STOCK");

        // هیچ سفارش نیمه‌کاری ساخته نشده است.
        (await DbContext.Order.CountAsync()).Should().Be(ordersBefore);
    }

    // ==================== R6 (فاز ۳) ====================

    [Fact]
    public async Task CancelOrder_CancelsAllSellerGroups_AndCreatesNoPayouts()
    {
        var (buyerUser, buyer) = await CreateCustomerAsync();
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        var productA = await CreateProductAsync("A", 100);
        var productB = await CreateProductAsync("B", 200);
        var (_, listingA) = await CreateListingAsync(sellerUserA.UserId, productA.ProductId, 100);
        var (_, listingB) = await CreateListingAsync(sellerUserB.UserId, productB.ProductId, 200);

        SetCurrentUser(buyerUser.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(
            buyer.CustomerId, "Buyer", "Tehran", "09120000000");
        await OrderService.AddItemToOrderAsync(
            order.OrderId, new BusinessLogic.DTOs.OrderItem.AddOrderItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await OrderService.AddItemToOrderAsync(
            order.OrderId, new BusinessLogic.DTOs.OrderItem.AddOrderItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        await OrderService.ConfirmOrderAsync(order.OrderId);

        var beforeCancel = await DbContext.OrderSellerGroup
            .Where(g => g.OrderId == order.OrderId).ToListAsync();
        beforeCancel.Should().HaveCount(2);
        beforeCancel.Should().OnlyContain(g => g.Status == SellerGroupStatus.Processing);

        await OrderService.CancelOrderAsync(order.OrderId);

        var afterCancel = await DbContext.OrderSellerGroup
            .Where(g => g.OrderId == order.OrderId).ToListAsync();
        afterCancel.Should().HaveCount(2);
        afterCancel.Should().OnlyContain(g => g.Status == SellerGroupStatus.Cancelled);

        var payouts = await DbContext.SellerPayout.ToListAsync();
        payouts.Should().BeEmpty();

        // تحویل سفارش لغوشده ممکن نیست.
        var act = () => OrderService.MarkDeliveredAsync(order.OrderId);
        await act.Should().ThrowAsync<BusinessException>();
    }

    // ==================== R9 — تراکنش Checkout ====================

    [Fact]
    public async Task Checkout_WhenAnyStepFails_RollsBackTheWholeOrder()
    {
        var (buyer, _) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync("Atomic", 50);
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, 50);

        await CartService.AddItemAsync(
            buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var ordersBefore = await DbContext.Order.AsNoTracking().CountAsync();

        // سرویس سفارش واقعاً یک ردیف Order در دیتابیس می‌نویسد و بعد شکست می‌خورد.
        var failingService = new OrderWritingThenFailingService(DbContext);

        var cartService = new CartService(
            GetService<IUnitOfWork>(),
            NullLogger<CartService>.Instance,
            GetService<IInventoryService>(),
            failingService);

        var act = () => cartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Rollback Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("TEST_CHECKOUT_FAILURE");

        // سفارشی که داخل تراکنش نوشته شد، پس از rollback نباید باقی بماند.
        (await DbContext.Order.AsNoTracking().CountAsync()).Should().Be(ordersBefore);

        // سبد هم باید دست‌نخورده بماند تا کاربر بتواند دوباره تلاش کند.
        var cart = await CartService.GetCartAsync(buyer.UserId, null);
        cart.Items.Should().HaveCount(1);
    }

    /// <summary>
    /// سرویس سفارش جعلی: یک Order واقعی در DbContext می‌نویسد و سپس استثنا می‌دهد.
    /// اگر checkout تراکنشی نبود، این ردیف برای همیشه باقی می‌ماند.
    /// </summary>
    private sealed class OrderWritingThenFailingService : IOrderService
    {
        private readonly AppDbContext _db;

        public OrderWritingThenFailingService(AppDbContext db) => _db = db;

        public async Task<OrderDto> CreateEmptyOrderAsync(
            int customerId,
            string shippingFullName,
            string shippingAddress,
            string shippingPhone,
            CancellationToken cancellationToken = default)
        {
            var order = new Order
            {
                CustomerId = customerId,
                ShippingFullName = shippingFullName,
                ShippingAddress = shippingAddress,
                ShippingPhoneNumber = shippingPhone,
                OrderDate = DateTime.UtcNow
            };

            _db.Order.Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            throw new BusinessException("شکست شبیه‌سازی‌شده پس از نوشتن سفارش.", "TEST_CHECKOUT_FAILURE");
        }

        public Task<OrderDto> AddItemToOrderAsync(int orderId, BusinessLogic.DTOs.OrderItem.AddOrderItemDto itemDto, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto> RemoveItemFromOrderAsync(int orderId, int orderItemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto> ConfirmOrderAsync(int orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderDto>> GetOrdersAsync(int userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDetailsDto?> GetOrderDetailsAsync(int userId, int orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto> MarkDeliveredAsync(int orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto> MarkReturnedAsync(int orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}