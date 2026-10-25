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

/// <summary>
/// Phase 3 — تقسیم سفارش بین فروشندگان و محاسبات مالی.
/// </summary>
public class OrderSplittingTests : BaseIntegrationTest
{
    private const decimal DefaultCommissionRate = 10m;

    private IOrderService OrderService => GetService<IOrderService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public OrderSplittingTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    /// <summary>Phase 6: هر Listing به یک واریانت وصل است.</summary>
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

    private async Task<(Customer customer, User user)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Split",
            LastName = "Buyer",
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

        return (customer, user);
    }

    private async Task<Product> CreateProductAsync(string name, decimal price)
    {
        var product = new Product { Name = name, Price = price, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        return product;
    }

    /// <summary>فروشنده + Listing + انبار + موجودی، همه با هم.</summary>
    private async Task<(Seller seller, SellerProduct listing)> CreateSellerWithListingAsync(
        int ownerUserId,
        int productId,
        decimal price,
        decimal quantity = 100,
        decimal? commissionRate = null)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"Split-{Guid.NewGuid():N}",
            Slug = $"split-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            CommissionRate = commissionRate
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var variant = await CreateVariantAsync(productId);

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
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

    private async Task<Order> ReloadOrderAsync(int orderId, bool includeGroups = true)
    {
        var query = DbContext.Order.AsQueryable();
        query = query.Include(o => o.OrderItems);
        if (includeGroups)
            query = query.Include(o => o.SellerGroups);

        return await query.SingleAsync(o => o.OrderId == orderId);
    }

    // ===================== تقسیم سفارش =====================

    [Fact]
    public async Task TwoSellersInOneOrder_CreatesTwoGroups_AndTotalEqualsSumOfSubTotals()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var productA = await CreateProductAsync("A", 50);
        var productB = await CreateProductAsync("B", 100);

        var (sellerA, listingA) = await CreateSellerWithListingAsync(sellerUserA.UserId, productA.ProductId, 50);
        var (sellerB, listingB) = await CreateSellerWithListingAsync(sellerUserB.UserId, productB.ProductId, 100);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingA.ListingId, Quantity = 2 });   // 100
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingB.ListingId, Quantity = 1 });   // 100

        var groups = await DbContext.OrderSellerGroup
            .Where(g => g.OrderId == order.OrderId)
            .OrderBy(g => g.SellerId)
            .ToListAsync();

        groups.Should().HaveCount(2);
        groups.Select(g => g.SellerId).Should().BeEquivalentTo(new[] { sellerA.SellerId, sellerB.SellerId });

        var groupA = groups.Single(g => g.SellerId == sellerA.SellerId);
        var groupB = groups.Single(g => g.SellerId == sellerB.SellerId);

        groupA.SubTotal.Should().Be(100m);
        groupB.SubTotal.Should().Be(100m);

        var reloaded = await ReloadOrderAsync(order.OrderId);
        reloaded.TotalAmount.Should().Be(groupA.SubTotal + groupB.SubTotal);
        reloaded.TotalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task TwoItemsFromSameSeller_ReuseSingleGroup()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var productA = await CreateProductAsync("A", 10);
        var productB = await CreateProductAsync("B", 20);

        var (seller, listingA) = await CreateSellerWithListingAsync(sellerUser.UserId, productA.ProductId, 10);
        var variantB = await CreateVariantAsync(productB.ProductId);

        var listingB = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variantB.VariantId,
            Price = 20,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listingB);
        await DbContext.SaveChangesAsync();

        // Listing دوم هم باید موجودی داشته باشد، وگرنه افزودن آیتم رد می‌شود.
        var warehouse = await DbContext.Warehouse.FirstAsync(w => w.SellerId == seller.SellerId);
        DbContext.Inventory.Add(new Inventory(listingB.ListingId, warehouse.WarehouseId, 100));
        await DbContext.SaveChangesAsync();

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingB.ListingId, Quantity = 3 });

        var groups = await DbContext.OrderSellerGroup.Where(g => g.OrderId == order.OrderId).ToListAsync();

        groups.Should().HaveCount(1);
        groups[0].SubTotal.Should().Be(70m); // 10 + 3*20
    }

    [Fact]
    public async Task RemoveLastItemOfSeller_RemovesItsGroup()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var productA = await CreateProductAsync("A", 50);
        var productB = await CreateProductAsync("B", 60);

        var (_, listingA) = await CreateSellerWithListingAsync(sellerUserA.UserId, productA.ProductId, 50);
        var (_, listingB) = await CreateSellerWithListingAsync(sellerUserB.UserId, productB.ProductId, 60);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        var itemA = await DbContext.OrderItem.FirstAsync(i => i.ListingId == listingA.ListingId);

        await OrderService.RemoveItemFromOrderAsync(order.OrderId, itemA.OrderItemId);

        var groups = await DbContext.OrderSellerGroup.Where(g => g.OrderId == order.OrderId).ToListAsync();
        groups.Should().HaveCount(1);
        groups[0].SubTotal.Should().Be(60m);
    }

    // ===================== کمیسیون =====================

    [Fact]
    public async Task ConfirmOrder_UsesSellerSpecificCommissionRate()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var product = await CreateProductAsync("P", 200);
        var (seller, listing) = await CreateSellerWithListingAsync(
            sellerUser.UserId, product.ProductId, 200, commissionRate: 15m);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 });

        await OrderService.ConfirmOrderAsync(order.OrderId);

        var group = await DbContext.OrderSellerGroup.SingleAsync(g => g.OrderId == order.OrderId);

        group.CommissionRate.Should().Be(15m);
        group.SubTotal.Should().Be(200m);
        group.CommissionAmount.Should().Be(30m);
        group.SellerPayable.Should().Be(170m);
        group.Status.Should().Be(SellerGroupStatus.Processing);
        group.SellerId.Should().Be(seller.SellerId);
    }

    [Fact]
    public async Task ConfirmOrder_FallsBackToPlatformDefaultRate()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var product = await CreateProductAsync("P", 250);
        var (_, listing) = await CreateSellerWithListingAsync(
            sellerUser.UserId, product.ProductId, 250, commissionRate: null);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 2 }); // 500

        await OrderService.ConfirmOrderAsync(order.OrderId);

        var group = await DbContext.OrderSellerGroup.SingleAsync(g => g.OrderId == order.OrderId);

        group.CommissionRate.Should().Be(DefaultCommissionRate);
        group.CommissionAmount.Should().Be(50m);   // 500 * 10%
        group.SellerPayable.Should().Be(450m);
    }

    [Fact]
    public async Task ConfirmOrder_SnapshotsRate_EvenIfSellerRateChangesLater()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var product = await CreateProductAsync("P", 100);
        var (seller, listing) = await CreateSellerWithListingAsync(
            sellerUser.UserId, product.ProductId, 100, commissionRate: 20m);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 });
        await OrderService.ConfirmOrderAsync(order.OrderId);

        // نرخ فروشنده بعد از تأیید سفارش عوض می‌شود.
        var trackedSeller = await DbContext.Seller.SingleAsync(s => s.SellerId == seller.SellerId);
        trackedSeller.CommissionRate = 5m;
        await DbContext.SaveChangesAsync();

        var group = await DbContext.OrderSellerGroup.SingleAsync(g => g.OrderId == order.OrderId);

        group.CommissionRate.Should().Be(20m);
        group.CommissionAmount.Should().Be(20m);
        group.SellerPayable.Should().Be(80m);
    }

    // ===================== تسویه =====================

    [Fact]
    public async Task MarkDelivered_CreatesOnePayoutPerGroup()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUserA, _) = await CreateCustomerAsync();
        var (sellerUserB, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var productA = await CreateProductAsync("A", 100);
        var productB = await CreateProductAsync("B", 200);

        var (sellerA, listingA) = await CreateSellerWithListingAsync(sellerUserA.UserId, productA.ProductId, 100);
        var (sellerB, listingB) = await CreateSellerWithListingAsync(sellerUserB.UserId, productB.ProductId, 200);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);

        var payouts = await DbContext.SellerPayout
            .Where(p => p.OrderSellerGroupId != null)
            .ToListAsync();

        payouts.Should().HaveCount(2);
        payouts.Single(p => p.SellerId == sellerA.SellerId).Amount.Should().Be(90m);  // 100 - 10%
        payouts.Single(p => p.SellerId == sellerB.SellerId).Amount.Should().Be(180m); // 200 - 10%

        payouts.Should().OnlyContain(p => p.Status == PayoutStatus.Pending);

        var groups = await DbContext.OrderSellerGroup.Where(g => g.OrderId == order.OrderId).ToListAsync();
        groups.Should().OnlyContain(g => g.Status == SellerGroupStatus.Delivered);

        var reloaded = await ReloadOrderAsync(order.OrderId, includeGroups: false);
        reloaded.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task MarkDelivered_BeforeConfirm_IsRejected()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");

        var ex = await FluentActions
            .Awaiting(() => OrderService.MarkDeliveredAsync(order.OrderId))
            .Should().ThrowAsync<Application.Exceptions.BusinessException>();

        ex.And.Code.Should().Be("ORDER_NOT_DELIVERABLE");
    }

    [Fact]
    public async Task MarkDelivered_Twice_DoesNotCreateDuplicatePayouts()
    {
        var (customer, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        SetCurrentUser(buyer.UserId);

        var product = await CreateProductAsync("P", 100);
        var (_, listing) = await CreateSellerWithListingAsync(sellerUser.UserId, product.ProductId, 100);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "T", "Addr", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId,
            new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 });

        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);

        await FluentActions
            .Awaiting(() => OrderService.MarkDeliveredAsync(order.OrderId))
            .Should().ThrowAsync<Application.Exceptions.BusinessException>();

        var payouts = await DbContext.SellerPayout.Where(p => p.OrderSellerGroupId != null).ToListAsync();
        payouts.Should().HaveCount(1);
    }
}