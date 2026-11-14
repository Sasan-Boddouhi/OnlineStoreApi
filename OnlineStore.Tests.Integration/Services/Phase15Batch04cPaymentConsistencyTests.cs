using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 4c — دو ناسازگاری باقی‌مانده از مسیر جدید پرداخت (Batch 4b):
///
///  1) Payment ابتدا Succeeded ذخیره می‌شود و سفارش جداگانه به‌روز می‌شود. اگر به‌روزرسانی سفارش
///     شکست بخورد، سفارش Pending می‌ماند و جاب «سفارش رهاشده» نباید آن را لغو کند
///     (لغو + آزادسازی موجودی برای سفارشی که پولش گرفته شده).
///  2) دریافت پول باید همان همگام‌سازی idempotent پس از برداشت را اجرا کند (Batch 41: ثبت دستی پرداخت حذف شد؛ مسیر درگاه)،
///     وگرنه سفارش Pending می‌ماند و ارسال/تحویل ممکن نیست.
/// </summary>
public class Phase15Batch04cPaymentConsistencyTests : ControllerIntegrationTestBase
{
    private const string ReviewAction = "Payment.ReviewRequired";

    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase15Batch04cPaymentConsistencyTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== 1) جاب سفارش رهاشده و پرداخت موفق ====================

    [Fact]
    public async Task AbandonedCleanup_PendingOrderWithSucceededPayment_IsNotCancelled_AndFlagsForReview()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var payment = await FulfilmentTestHelper.AddSucceededPaymentAsync(DbContext, seeded.OrderId);
        await AgeOrderAsync(seeded.OrderId);

        var availableBefore = await InventoryService.GetAvailableAsync(seeded.ListingIds[0]);

        var cancelled = await NewCleanup().RunCleanupAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Pending);
        (await InventoryService.GetAvailableAsync(seeded.ListingIds[0])).Should().Be(availableBefore, "stock stays reserved");
        (await PaymentStateAsync(payment.PaymentId)).Should().Be(PaymentState.Succeeded);

        var review = await ReviewEntriesAsync(payment.PaymentId);
        review.Should().ContainSingle()
            .Which.Should().Contain("abandoned-order cleanup").And.Contain($"order={seeded.OrderId}");
    }

    [Fact]
    public async Task AbandonedCleanup_RunTwice_RecordsOneReviewEntryPerPayment()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var payment = await FulfilmentTestHelper.AddSucceededPaymentAsync(DbContext, seeded.OrderId);
        await AgeOrderAsync(seeded.OrderId);

        await NewCleanup().RunCleanupAsync();
        await NewCleanup().RunCleanupAsync();

        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
        (await ReviewEntriesAsync(payment.PaymentId)).Should().ContainSingle("the job runs every 10 minutes and must not flood the audit log");
    }

    [Fact]
    public async Task AbandonedCleanup_PendingOrderWithOnlyOpenPayment_IsStillCancelled()
    {
        // رفتار موجود (Batch 4) حفظ می‌شود: پرداخت باز و ناتمام مانع لغو نیست.
        var seeded = await CreateOrderAsync(sellers: 1);
        await AgeOrderAsync(seeded.OrderId);

        var cancelled = await NewCleanup().RunCleanupAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);
    }

    // ==================== 2) همگام‌سازی سفارش پس از دریافت پول ====================
    //
    // Batch 41 (FIN-15): ثبت دستی پرداخت (RecordPaymentAsync) حذف شد؛ هیچ فراخوانی در کد محصول نداشت و می‌توانست
    // فاکتور را با هر مبلغی «پرداخت‌شده» کند. دو تست زیر همان انتظارها را روی تنها مسیر باقی‌ماندهٔ دریافت پول
    // (درگاه: Initiate + Verify) نگه می‌دارند. دو تست دیگرِ این بخش فقط رفتار همان متد حذف‌شده را می‌سنجیدند و
    // معادل درگاهی ندارند، چون درگاه پیش از برداشت جلوی آن حالت‌ها را می‌گیرد:
    //   • «ثبت دستی روی سفارش لغوشده» ⇒ Verify برای سفارش لغوشده اصلاً صدا زده نمی‌شود
    //     (Phase15Batch05: Verify_AfterCustomerCancelledOrder_DoesNotCallGateway_AndIsAudited)؛ برداشتی که پیش از لغو
    //     آغاز شده بود در Phase15Batch04b: Verify_OrderCancelledWhilePaymentPending_RecordsSucceeded_AndFlagsForRefundReview.
    //   • «ثبت دستی دوباره» ⇒ پرداخت دوم برای سفارش پرداخت‌شده رد می‌شود
    //     (Phase15Batch05: Initiate_OnPaidOrder_Returns409OrderAlreadyPaid و Verify_SecondPaymentOfPaidOrder_IsNotVerified).

    [Fact]
    public async Task GatewayPayment_ConfirmsOrder_AppliesCommission_AndMarksInvoicePaid()
    {
        var seeded = await CreateOrderAsync(sellers: 2);

        var paid = await PayThroughGatewayAsync(seeded);

        paid.State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Processing);
        (await CommissionsAsync(seeded.OrderId)).Should().OnlyContain(c => c > 0m);
        (await DbContext.Invoice.AsNoTracking().SingleAsync(i => i.OrderId == seeded.OrderId)).Status
            .Should().Be(InvoiceStatus.Paid);
        (await ReviewEntriesAsync(paid.PaymentId)).Should().BeEmpty();
    }

    [Fact]
    public async Task GatewayPayment_OrderCanBeShipped_AndIsNotCancelledByCleanup()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        await PayThroughGatewayAsync(seeded);
        await AgeOrderAsync(seeded.OrderId);

        var cancelled = await NewCleanup().RunCleanupAsync();
        cancelled.Should().Be(0);

        var (seller, groupId) = seeded.Groups[0];
        SetCurrentUser(seller.UserId, "Seller");
        var shipped = await DashboardService.ShipSellerGroupAsync(
            seeded.OrderId, groupId, new ShipSellerGroupRequest { CarrierName = "Tipax", TrackingNumber = $"TPX-{Guid.NewGuid():N}"[..20] });

        shipped.GroupStatus.Should().Be(nameof(SellerGroupStatus.Shipped));
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Shipped);
    }

    // ==================== Helpers ====================

    private sealed record SeededOrder(
        int OrderId,
        int BuyerUserId,
        IReadOnlyList<(Seller Seller, int GroupId)> Groups,
        IReadOnlyList<int> ListingIds);

    private AbandonedOrderCleanupService NewCleanup()
        => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AbandonedOrderCleanupService>.Instance);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private async Task AgeOrderAsync(int orderId)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        order.OrderDate = DateTime.UtcNow.AddMinutes(-75);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>Batch 41: دریافت پول از تنها مسیر باقی‌مانده — درگاه (Initiate + Verify) به نام خریدار.</summary>
    private async Task<PaymentDto> PayThroughGatewayAsync(SeededOrder seeded)
    {
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        return await PaymentService.VerifyAsync(initiation.Authority!);
    }

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<PaymentState> PaymentStateAsync(int paymentId)
        => DbContext.Payment.AsNoTracking().Where(p => p.PaymentId == paymentId).Select(p => p.State).SingleAsync();

    private Task<List<SellerGroupStatus>> GroupStatusesAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).OrderBy(g => g.OrderSellerGroupId).Select(g => g.Status).ToListAsync();

    private Task<List<decimal>> CommissionsAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).OrderBy(g => g.OrderSellerGroupId)
            .Select(g => g.CommissionAmount).ToListAsync();

    private Task<List<string>> ReviewEntriesAsync(int paymentId)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == ReviewAction && a.EntityName == nameof(Payment) && a.EntityId == paymentId.ToString())
            .Select(a => a.Details ?? string.Empty)
            .ToListAsync();

    private async Task<SeededOrder> CreateOrderAsync(int sellers)
    {
        var listings = new List<(Seller Seller, int ListingId)>();
        for (var i = 0; i < sellers; i++)
        {
            var (sellerUser, _) = await CreateUserAsync();
            listings.Add(await CreateSellerWithListingAsync(sellerUser, 100m * (i + 1)));
        }

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B4c Buyer", "Tehran", "09120000000");
        foreach (var (_, listingId) in listings)
            await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });

        var groups = await DbContext.OrderSellerGroup.AsNoTracking().Where(g => g.OrderId == order.OrderId).ToListAsync();
        DbContext.ChangeTracker.Clear();

        return new SeededOrder(
            order.OrderId,
            buyerUser.UserId,
            listings.Select(l => (l.Seller, groups.Single(g => g.SellerId == l.Seller.SellerId).OrderSellerGroupId)).ToList(),
            listings.Select(l => l.ListingId).ToList());
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B4c",
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

    private async Task<(Seller Seller, int ListingId)> CreateSellerWithListingAsync(User sellerUser, decimal price)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B4cShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B4c-{Guid.NewGuid():N}",
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
            RecipientFirstName = "B4c",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 10m));
        await DbContext.SaveChangesAsync();

        return (seller, listing.ListingId);
    }
}
