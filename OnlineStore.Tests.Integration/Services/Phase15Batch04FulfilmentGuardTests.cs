using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using DataLayer.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 4 — گاردهای ارسال و تحویل گروه فروشنده (F-02، F-18، F-33).
///
/// تعریف «پرداخت‌شده»: سفارش در وضعیت Processing/Shipped/Delivered است
/// و دست‌کم یک Payment با وضعیت Succeeded دارد.
/// </summary>
public class Phase15Batch04FulfilmentGuardTests : ControllerIntegrationTestBase
{
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase15Batch04FulfilmentGuardTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== ارسال ====================

    [Fact]
    public async Task Ship_UnpaidMultiSellerOrder_Returns409OrderNotPaid_AndChangesNothing()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PAID");
        ex.Which.StatusCode.Should().Be(409);

        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Pending);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Ship_ConfirmedButNeverPaidOrder_Returns409OrderNotPaid()
    {
        // ادمین می‌تواند سفارش را بی‌پرداخت به Processing ببرد؛ وضعیت سفارش به‌تنهایی «پرداخت» نیست.
        var seeded = await CreateOrderAsync(sellers: 2, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PAID");
        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Processing);
    }

    [Fact]
    public async Task Ship_CancelledUnpaidOrder_Returns409OrderCancelled()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        await OrderService.CancelOrderAsync(seeded.OrderId);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_CANCELLED");
        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Cancelled);
    }

    [Fact]
    public async Task Ship_AfterRefundOfProcessingOrder_Returns409OrderCancelled_AndChangesNothing()
    {
        var seeded = await CreateOrderAsync(sellers: 2, pay: true, confirm: true);

        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(seeded.PaymentId!.Value, "duplicate charge");

        var (seller, groupId) = seeded.Groups[0];
        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_CANCELLED");
        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Processing);
    }

    // ==================== تحویل ====================

    [Fact]
    public async Task Deliver_UnpaidMultiSellerOrder_Returns409OrderNotPaid_AndCreatesNoPayout()
    {
        // همان سناریوی سوءاستفادهٔ F-02.
        var seeded = await CreateOrderAsync(sellers: 2);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PAID");

        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Pending);
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task Deliver_PaidGroupNeverShipped_Returns409GroupNotShipped_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("GROUP_NOT_SHIPPED");

        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Processing);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task Deliver_ShippedGroupOfCancelledOrder_Returns409OrderCancelled_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 2, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        await OrderService.CancelOrderAsync(seeded.OrderId);

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_CANCELLED");
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task Deliver_ShippedOrderAfterRefund_Returns409OrderRefunded_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Shipped);

        // برگشت وجه سفارش Shipped وضعیت سفارش را دست نمی‌زند؛ فقط پرداخت Refunded می‌شود.
        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(seeded.PaymentId!.Value, "lost parcel");

        SetCurrentUser(seller.UserId, "Seller");
        var act = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_REFUNDED");

        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Shipped);
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task HappyPath_PaidShipDeliver_CreatesExactlyOnePayoutWithSellerPayable()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());
        var delivered = await DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        delivered.GroupStatus.Should().Be(nameof(SellerGroupStatus.Delivered));
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Delivered);

        var group = await DbContext.OrderSellerGroup.AsNoTracking().SingleAsync(g => g.OrderSellerGroupId == groupId);
        var payouts = await DbContext.SellerPayout.AsNoTracking()
            .Where(p => p.OrderSellerGroupId == groupId).ToListAsync();

        payouts.Should().ContainSingle();
        payouts[0].SellerId.Should().Be(seller.SellerId);
        payouts[0].Status.Should().Be(PayoutStatus.Pending);
        payouts[0].Amount.Should().Be(group.SellerPayable);
        payouts[0].Amount.Should().Be(90m, "price 100 minus the 10% platform commission");
    }

    [Fact]
    public async Task Deliver_Twice_SecondCallIsRejected_AndOnlyOnePayoutExists()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());
        await DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var again = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await again.Should().ThrowAsync<AppException>();
        ex.Which.Code.Should().Be("GROUP_ALREADY_DELIVERED");
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Deliver_RacingRequestCreatesPayoutFirst_Returns409_AndExactlyOnePayoutExists()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (seller, groupId) = seeded.Groups[0];

        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(seeded.OrderId, groupId, ShipRequest());

        // درخواست دوم هم‌زمان: درست پیش از ذخیره، درخواست رقیب payout همین گروه را ثبت می‌کند.
        using var scope = Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var competitor = new CompetingPayoutInterceptor(Factory.Services, groupId, seller.SellerId);
        var interceptors = sp.GetServices<ISaveChangesInterceptor>().Append(competitor).ToList();

        await using var racingContext = new TestAppDbContext(
            sp.GetRequiredService<DbContextOptions<AppDbContext>>(), interceptors);

        var currentUser = (FakeCurrentUserService)sp.GetRequiredService<ICurrentUserService>();
        currentUser.CurrentUserId = seller.UserId;
        currentUser.CurrentUserRole = "Seller";

        var racingService = new SellerDashboardService(
            new UnitOfWork(racingContext), currentUser, NullLogger<SellerDashboardService>.Instance);

        var act = () => racingService.DeliverSellerGroupAsync(seeded.OrderId, groupId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("CONCURRENCY_CONFLICT");
        competitor.Fired.Should().BeTrue();

        (await DbContext.SellerPayout.AsNoTracking().CountAsync(p => p.OrderSellerGroupId == groupId))
            .Should().Be(1);
    }

    // ==================== مسیر ادمین ====================

    [Fact]
    public async Task AdminDeliver_ConfirmedButNeverPaidOrder_Returns409OrderNotPaid_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 2, confirm: true);

        SetCurrentUser(1, "Admin");
        var act = () => OrderService.MarkDeliveredAsync(seeded.OrderId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PAID");

        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task AdminDeliver_PaidButGroupsNotShipped_Returns409GroupNotShipped_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 2, pay: true, confirm: true);

        SetCurrentUser(1, "Admin");
        var act = () => OrderService.MarkDeliveredAsync(seeded.OrderId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("GROUP_NOT_SHIPPED");

        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task AdminDeliver_PaidAndShipped_CreatesOnePayoutPerGroup()
    {
        var seeded = await CreateOrderAsync(sellers: 2, pay: true, confirm: true);
        await FulfilmentTestHelper.ShipAllGroupsAsync(DbContext, seeded.OrderId);

        SetCurrentUser(1, "Admin");
        await OrderService.MarkDeliveredAsync(seeded.OrderId);

        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Delivered);

        var payouts = await DbContext.SellerPayout.AsNoTracking()
            .Where(p => p.OrderSellerGroup!.OrderId == seeded.OrderId).ToListAsync();
        payouts.Should().HaveCount(2);
        payouts.Select(p => p.OrderSellerGroupId).Should().OnlyHaveUniqueItems();
    }

    // ==================== HTTP (نگاشت میان‌افزار) ====================

    [Fact]
    public async Task Http_ShipUnpaidOrder_Returns409WithOrderNotPaidCode()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var (_, groupId) = seeded.Groups[0];

        await AuthorizeAsAdminAsync();
        var response = await Client.PostAsJsonAsync(
            $"/api/v1/sellers/orders/{seeded.OrderId}/groups/{groupId}/ship", ShipRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(response)).Should().Be("ORDER_NOT_PAID");
        (await GroupStatusAsync(groupId)).Should().Be(SellerGroupStatus.Pending);
    }

    [Fact]
    public async Task Http_DeliverNeverShippedGroup_Returns409WithGroupNotShippedCode()
    {
        var seeded = await CreateOrderAsync(sellers: 1, pay: true, confirm: true);
        var (_, groupId) = seeded.Groups[0];

        await AuthorizeAsAdminAsync();
        var response = await Client.PostAsync(
            $"/api/v1/sellers/orders/{seeded.OrderId}/groups/{groupId}/deliver", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(response)).Should().Be("GROUP_NOT_SHIPPED");
        (await PayoutCountAsync(seeded.OrderId)).Should().Be(0);
    }

    // ==================== F-18: پاک‌سازی سفارش‌های رهاشده ====================

    [Fact]
    public async Task AbandonedCleanup_OnePoisonedOrder_DoesNotStopTheNextOrder()
    {
        var poisoned = await CreateOrderAsync(sellers: 1);
        var healthy = await CreateOrderAsync(sellers: 1);

        // دادهٔ باقی‌مانده از حفرهٔ F-02: گروهِ سفارش Pending که «تحویل» شده است.
        var poisonedGroup = await DbContext.OrderSellerGroup.AsTracking()
            .SingleAsync(g => g.OrderSellerGroupId == poisoned.Groups[0].GroupId);
        DbContext.Entry(poisonedGroup).Property(g => g.Status).CurrentValue = SellerGroupStatus.Delivered;

        foreach (var orderId in new[] { poisoned.OrderId, healthy.OrderId })
        {
            var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
            order.OrderDate = DateTime.UtcNow.AddMinutes(-75);
        }
        await DbContext.SaveChangesAsync();

        var healthyListing = healthy.ListingIds[0];
        var availableBefore = await InventoryService.GetAvailableAsync(healthyListing);

        var cleanup = new AbandonedOrderCleanupService(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AbandonedOrderCleanupService>.Instance);

        var cancelled = await cleanup.RunCleanupAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(healthy.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await GroupStatusAsync(healthy.Groups[0].GroupId)).Should().Be(SellerGroupStatus.Cancelled);
        (await OrderStatusAsync(poisoned.OrderId)).Should().Be(OrderStatus.Pending);
        (await InventoryService.GetAvailableAsync(healthyListing)).Should().Be(availableBefore + 1);
    }

    // ==================== F-33: داشبورد ====================

    [Fact]
    public async Task DashboardSummary_CountsOnlyPaidOrders()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var (seller, listing) = await CreateSellerWithListingAsync(sellerUser, 100m);

        await CreateOrderForListingsAsync(new[] { listing.ListingId }, pay: true, confirm: true);   // پرداخت‌شده
        await CreateOrderForListingsAsync(new[] { listing.ListingId }, pay: false, confirm: false); // Pending
        await CreateOrderForListingsAsync(new[] { listing.ListingId }, pay: false, confirm: true);  // تأییدشده ولی پرداخت‌نشده

        SetCurrentUser(sellerUser.UserId, "Seller");
        var summary = await DashboardService.GetSalesSummaryAsync(seller.SellerId, null, null);

        summary.TotalOrders.Should().Be(1);
        summary.TotalSales.Should().Be(100m);
        summary.NetEarnings.Should().Be(90m);
    }

    // ==================== ابزار ====================

    private sealed record SeededOrder(
        int OrderId,
        int BuyerUserId,
        int? PaymentId,
        IReadOnlyList<(Seller Seller, int GroupId)> Groups,
        IReadOnlyList<int> ListingIds);

    private static ShipSellerGroupRequest ShipRequest()
        => new() { CarrierName = "Tipax", TrackingNumber = $"TPX-{Guid.NewGuid():N}"[..20] };

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private async Task AuthorizeAsAdminAsync()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<SellerGroupStatus> GroupStatusAsync(int groupId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderSellerGroupId == groupId).Select(g => g.Status).SingleAsync();

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking()
            .Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<int> PayoutCountAsync(int orderId)
        => DbContext.SellerPayout.AsNoTracking()
            .CountAsync(p => p.OrderSellerGroup != null && p.OrderSellerGroup.OrderId == orderId);

    private async Task<SeededOrder> CreateOrderAsync(int sellers, bool pay = false, bool confirm = false)
    {
        var listings = new List<(Seller Seller, SellerProduct Listing)>();
        for (var i = 0; i < sellers; i++)
        {
            var (sellerUser, _) = await CreateCustomerAsync();
            listings.Add(await CreateSellerWithListingAsync(sellerUser, 100m * (i + 1)));
        }

        var (orderId, buyerUserId, paymentId) = await CreateOrderForListingsAsync(
            listings.Select(l => l.Listing.ListingId).ToList(), pay, confirm);

        var groups = await DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).ToListAsync();

        return new SeededOrder(
            orderId,
            buyerUserId,
            paymentId,
            listings.Select(l => (l.Seller, groups.Single(g => g.SellerId == l.Seller.SellerId).OrderSellerGroupId)).ToList(),
            listings.Select(l => l.Listing.ListingId).ToList());
    }

    private async Task<(int OrderId, int BuyerUserId, int? PaymentId)> CreateOrderForListingsAsync(
        IReadOnlyList<int> listingIds, bool pay, bool confirm)
    {
        var (buyerUser, buyer) = await CreateCustomerAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B4 Buyer", "Tehran", "09120000000");
        foreach (var listingId in listingIds)
            await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });

        int? paymentId = null;
        if (pay)
            paymentId = (await FulfilmentTestHelper.AddSucceededPaymentAsync(DbContext, order.OrderId)).PaymentId;

        if (confirm)
            await OrderService.ConfirmOrderAsync(order.OrderId);

        return (order.OrderId, buyerUser.UserId, paymentId);
    }

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "B4",
            LastName = "User",
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

        return (user, customer);
    }

    private async Task<(Seller Seller, SellerProduct Listing)> CreateSellerWithListingAsync(User sellerUser, decimal price)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B4Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var product = new Product
        {
            Name = $"B4-{Guid.NewGuid():N}",
            Price = price,
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
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B4",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"W-{Guid.NewGuid():N}",
            SellerId = seller.SellerId,
            AddressId = address.AddressId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 10m));
        await DbContext.SaveChangesAsync();

        return (seller, listing);
    }

    /// <summary>
    /// شبیه‌سازی قطعی رقابت: درست پیش از ذخیرهٔ تحویل، یک درخواست رقیب (DbContext جدا)
    /// payout همان گروه را ثبت می‌کند؛ همان چیزی که دو تحویل هم‌زمان در عمل می‌سازند.
    /// </summary>
    private sealed class CompetingPayoutInterceptor : SaveChangesInterceptor
    {
        private readonly IServiceProvider _root;
        private readonly int _groupId;
        private readonly int _sellerId;

        public CompetingPayoutInterceptor(IServiceProvider root, int groupId, int sellerId)
        {
            _root = root;
            _groupId = groupId;
            _sellerId = sellerId;
        }

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var addsPayout = eventData.Context!.ChangeTracker.Entries<SellerPayout>()
                .Any(e => e.State == EntityState.Added && e.Entity.OrderSellerGroupId == _groupId);

            if (!Fired && addsPayout)
            {
                Fired = true;

                using var scope = _root.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.SellerPayout.Add(new SellerPayout(_sellerId, _groupId, 1m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
                await db.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
