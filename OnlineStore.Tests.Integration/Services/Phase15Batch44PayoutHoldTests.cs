using System.Net.Http.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 44 (FIN-02، F-09، FIN-13) — نگه‌داشت تسویه و کسر بدهی از آن.
///  • FIN-02: تسویهٔ هر بسته از «تحویل + ۷ روز» قابل پرداخت است؛ پیش از آن 409 PAYOUT_ON_HOLD.
///  • F-09: بدهی باز فروشنده پیش از پرداخت در فهرست دیده می‌شود (debtOffsetAmount / netAmount) و همان روی تسویه ثبت می‌شود؛
///    مبلغ اصلی بدهی ثابت می‌ماند و فقط مبلغ تسویه‌شده بالا می‌رود.
///  • FIN-13: رکورد حسابرسی بدهی بازپس‌گیری شناسهٔ خودِ بدهی را دارد.
/// ساعت: <see cref="TestTimeProvider"/> کارخانهٔ تست (هر تست از «اکنون» واقعی شروع می‌کند).
/// </summary>
public class Phase15Batch44PayoutHoldTests : ControllerIntegrationTestBase
{
    private const decimal InitialStock = 10m;

    private IOrderService OrderService => GetService<IOrderService>();
    private ICartService CartService => GetService<ICartService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private ISellerPayoutService PayoutService => GetService<ISellerPayoutService>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private DateTime ClockNow => Factory.Clock.GetUtcNow().UtcDateTime;

    public Phase15Batch44PayoutHoldTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    // ==================== FIN-02: نگه‌داشت ====================

    [Fact]
    public async Task Pay_OneDayAfterDelivery_Returns409PayoutOnHold_AndLeavesPayoutPending()
    {
        var seeded = await CreateOrderAsync(prices: 100_000m);
        await PayShipDeliverAllAsync(seeded);
        var payout = await SinglePayoutAsync(seeded, 0);
        var deliveredAt = await DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderSellerGroupId == seeded.Groups[0].GroupId).Select(g => g.DeliveredAt!.Value).SingleAsync();
        payout.AvailableAtUtc.Should().Be(deliveredAt.AddDays(7), "تحویل + PayoutSettings:HoldDays (۷)");

        var adminToken = await GetAdminTokenAsync();
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        Client.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        var response = await Client.PostAsJsonAsync(
            $"/api/v1/seller-payouts/{payout.SellerPayoutId}/pay", new MarkPayoutPaidRequest { Reference = "BANK-EARLY" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PAYOUT_ON_HOLD");
        DbContext.ChangeTracker.Clear();
        (await SinglePayoutAsync(seeded, 0)).Status.Should().Be(PayoutStatus.Pending);
    }

    [Fact]
    public async Task Pay_OnDayEight_IsPaid_AtTheTestClock()
    {
        var seeded = await CreateOrderAsync(prices: 100_000m);
        await PayShipDeliverAllAsync(seeded);
        var payout = await SinglePayoutAsync(seeded, 0);

        Factory.Clock.Advance(TimeSpan.FromDays(8));
        SetCurrentUser(1, "Admin");
        var paid = await PayoutService.MarkAsPaidAsync(payout.SellerPayoutId, "BANK-DAY8");

        paid.Status.Should().Be(nameof(PayoutStatus.Paid));
        paid.PaidAt.Should().Be(ClockNow);
        paid.NetAmount.Should().Be(payout.Amount);
        paid.DebtOffsetAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ReturnInsideTheHoldWindow_CancelsThePendingPayout_AndCreatesNoDebt()
    {
        var seeded = await CreateOrderAsync(prices: 100_000m);
        await PayShipDeliverAllAsync(seeded);

        Factory.Clock.Advance(TimeSpan.FromDays(3));
        SetCurrentUser(1, "Admin");
        await OrderService.MarkReturnedAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        (await SinglePayoutAsync(seeded, 0)).Status.Should().Be(PayoutStatus.Cancelled);
        (await DbContext.SellerDebt.AsNoTracking().AnyAsync(d => d.SellerId == seeded.Groups[0].Seller.SellerId))
            .Should().BeFalse("تسویهٔ پرداخت‌نشده لغو می‌شود؛ بدهی فقط برای تسویهٔ Paid ساخته می‌شود");
    }

    // ==================== F-09: کسر بدهی ====================

    [Fact]
    public async Task Debt300_Payout1000_ShowsNet700BeforeAndAfterPay()
    {
        var (seller, sellerUserId) = await CreateSellerAsync();
        var debt = await AddDebtAsync(seller.SellerId, 300m);
        var payoutId = await AddAvailablePayoutAsync(seller.SellerId, 1_000m);

        SetCurrentUser(1, "Admin");
        var before = (await PayoutService.GetAllPayoutsAsync()).Single(p => p.SellerPayoutId == payoutId);
        before.Amount.Should().Be(1_000m);
        before.DebtOffsetAmount.Should().Be(300m);
        before.NetAmount.Should().Be(700m, "ادمین باید پیش از پرداخت مبلغ خالص را ببیند");

        SetCurrentUser(sellerUserId, "Seller");
        (await PayoutService.GetMyPayoutsAsync()).Single().NetAmount.Should().Be(700m);
        var summary = await DashboardService.GetSalesSummaryAsync(seller.SellerId, null, null);
        summary.PendingPayouts.Should().Be(1_000m);
        summary.OutstandingDebt.Should().Be(300m);
        summary.PendingPayoutsNet.Should().Be(700m);

        SetCurrentUser(1, "Admin");
        var paid = await PayoutService.MarkAsPaidAsync(payoutId, "BANK-700", expectedNetAmount: 700m);

        paid.NetAmount.Should().Be(700m);
        paid.DebtOffsetAmount.Should().Be(300m);
        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.SellerPayout.AsNoTracking().SingleAsync(p => p.SellerPayoutId == payoutId);
        stored.Amount.Should().Be(1_000m, "مبلغ ناخالص دست نمی‌خورد");
        stored.NetAmount.Should().Be(700m);
        stored.DebtOffsetAmount.Should().Be(300m);
        (await PayoutService.GetAllPayoutsAsync()).Single(p => p.SellerPayoutId == payoutId).NetAmount.Should().Be(700m);

        var settled = await DbContext.SellerDebt.AsNoTracking().SingleAsync(d => d.SellerDebtId == debt.SellerDebtId);
        settled.OriginalAmount.Should().Be(300m);
        settled.SettledAmount.Should().Be(300m);
        settled.IsSettled.Should().BeTrue();
        settled.SettledAtUtc.Should().Be(ClockNow);
    }

    [Fact]
    public async Task PartialSettlement_KeepsTheOriginalAmount_AndTheNextPayoutSettlesTheRest()
    {
        var (seller, _) = await CreateSellerAsync();
        var debt = await AddDebtAsync(seller.SellerId, 1_500m);
        var first = await AddAvailablePayoutAsync(seller.SellerId, 1_000m);

        SetCurrentUser(1, "Admin");
        var firstPaid = await PayoutService.MarkAsPaidAsync(first, "BANK-P1");
        firstPaid.DebtOffsetAmount.Should().Be(1_000m);
        firstPaid.NetAmount.Should().Be(0m, "همهٔ تسویه صرف بدهی شد");

        DbContext.ChangeTracker.Clear();
        var partly = await DbContext.SellerDebt.AsNoTracking().SingleAsync(d => d.SellerDebtId == debt.SellerDebtId);
        partly.OriginalAmount.Should().Be(1_500m, "مبلغ اصلی بدهی تغییر نمی‌کند (پیش‌تر به ۵۰۰ بازنویسی می‌شد)");
        partly.SettledAmount.Should().Be(1_000m);
        partly.IsSettled.Should().BeFalse();
        partly.SettledAtUtc.Should().BeNull();

        var second = await AddAvailablePayoutAsync(seller.SellerId, 800m);
        var secondPaid = await PayoutService.MarkAsPaidAsync(second, "BANK-P2");
        secondPaid.DebtOffsetAmount.Should().Be(500m);
        secondPaid.NetAmount.Should().Be(300m);

        DbContext.ChangeTracker.Clear();
        var done = await DbContext.SellerDebt.AsNoTracking().SingleAsync(d => d.SellerDebtId == debt.SellerDebtId);
        done.OriginalAmount.Should().Be(1_500m);
        done.SettledAmount.Should().Be(1_500m);
        done.IsSettled.Should().BeTrue();

        (await DbContext.AuditLog.AsNoTracking()
                .CountAsync(a => a.Action == "SellerDebt.Settled" && a.EntityId == debt.SellerDebtId.ToString()))
            .Should().Be(2, "هر کسر از بدهی با شناسهٔ بدهی حسابرسی می‌شود");
    }

    [Fact]
    public async Task Pay_WithAStaleExpectedNetAmount_Returns409_AndChangesNothing()
    {
        var (seller, _) = await CreateSellerAsync();
        var payoutId = await AddAvailablePayoutAsync(seller.SellerId, 1_000m);

        // ادمین فهرست را دید (خالص ۱۰۰۰) و سپس بدهی تازه‌ای ثبت شد.
        var debt = await AddDebtAsync(seller.SellerId, 250m);

        SetCurrentUser(1, "Admin");
        var act = () => PayoutService.MarkAsPaidAsync(payoutId, "BANK-STALE", expectedNetAmount: 1_000m);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("PAYOUT_NET_CHANGED");

        DbContext.ChangeTracker.Clear();
        (await DbContext.SellerPayout.AsNoTracking().SingleAsync(p => p.SellerPayoutId == payoutId)).Status
            .Should().Be(PayoutStatus.Pending);
        (await DbContext.SellerDebt.AsNoTracking().SingleAsync(d => d.SellerDebtId == debt.SellerDebtId)).SettledAmount
            .Should().Be(0m);
    }

    // ==================== FIN-13 ====================

    [Fact]
    public async Task ReturnOfAPaidPayout_WritesTheDebtAuditRow_WithTheDebtId()
    {
        // بدهی بی‌ربطی پیش‌تر ثبت می‌شود تا شناسهٔ بدهی تازه با شناسهٔ فروشنده یکی نباشد.
        var (other, _) = await CreateSellerAsync();
        await AddDebtAsync(other.SellerId, 1m);
        await AddDebtAsync(other.SellerId, 1m);

        var seeded = await CreateOrderAsync(prices: 100_000m);
        await PayShipDeliverAllAsync(seeded);
        var payout = await SinglePayoutAsync(seeded, 0);
        Factory.Clock.Advance(TimeSpan.FromDays(8));
        SetCurrentUser(1, "Admin");
        await PayoutService.MarkAsPaidAsync(payout.SellerPayoutId, "BANK-FIN13");

        await OrderService.MarkReturnedAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        var debt = await DbContext.SellerDebt.AsNoTracking().SingleAsync(d => d.SellerId == seeded.Groups[0].Seller.SellerId);
        debt.OriginalAmount.Should().Be(payout.Amount);
        debt.SellerDebtId.Should().NotBe(debt.SellerId);

        var audit = await DbContext.AuditLog.AsNoTracking().SingleAsync(a => a.Action == "SellerDebt.Created");
        audit.EntityName.Should().Be(nameof(SellerDebt));
        audit.EntityId.Should().Be(debt.SellerDebtId.ToString());
        audit.Details.Should().Contain($"seller={debt.SellerId}");
    }

    // ==================== گردش ریالی کامل یک سفارش دو فروشنده ====================

    /// <summary>
    /// سفارش دو فروشنده با کوپن ۱۰٪، ارسال ۵۰٬۰۰۰ و کمیسیون ۱۰٪، و بدهی ۱۰۰٬۰۰۰ فروشندهٔ A از مرجوعی قبلی.
    /// Batch 45 (N4/N5، اصلاحیهٔ مالک): ارسال برای هر بسته (۲ × ۵۰٬۰۰۰) و به سهم فروشنده افزوده می‌شود؛ کوپن پلتفرم
    /// از فروشنده کم نمی‌شود. سهم = SubTotal − کمیسیون (روی قیمت پیش از کوپن) + ارسال بسته.
    /// مقادیر پیش از Batch 45: ارسال ۵۰٬۰۰۰ برای کل سفارش، جمع ۹۵۰٬۰۰۰، سهم‌ها ۴۵۵٬۰۰۰ / ۲۹۵٬۰۰۰.
    /// </summary>
    [Fact]
    public async Task TwoSellerOrder_RialByRial_HoldThenNetOfDebt()
    {
        var shipping = new ShippingMethod { Name = "Post", Slug = $"post-{Guid.NewGuid():N}"[..20], BaseCost = 50_000m, IsActive = true };
        var coupon = new Coupon { Code = $"B44{Guid.NewGuid():N}"[..12].ToUpperInvariant(), DiscountPercentage = 10m, IsActive = true };
        DbContext.ShippingMethod.Add(shipping);
        DbContext.Coupon.Add(coupon);
        await DbContext.SaveChangesAsync();

        var seeded = await CreateOrderAsync(prices: new[] { 600_000m, 400_000m }, couponCode: coupon.Code, shippingMethodId: shipping.ShippingMethodId);
        var sellerA = seeded.Groups[0].Seller;
        var sellerB = seeded.Groups[1].Seller;

        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        order.DiscountAmount.Should().Be(100_000m);
        order.ShippingCost.Should().Be(100_000m, "Batch 45 (N4): ۲ بسته × ۵۰٬۰۰۰");
        order.TotalAmount.Should().Be(1_000_000m, "۱٬۰۰۰٬۰۰۰ − ۱۰۰٬۰۰۰ کوپن + ۱۰۰٬۰۰۰ ارسال");

        await AddDebtAsync(sellerA.SellerId, 100_000m);
        await PayShipDeliverAllAsync(seeded);

        var groups = await DbContext.OrderSellerGroup.AsNoTracking().Where(g => g.OrderId == seeded.OrderId).ToListAsync();
        var groupA = groups.Single(g => g.SellerId == sellerA.SellerId);
        var groupB = groups.Single(g => g.SellerId == sellerB.SellerId);
        (groupA.SubTotal, groupA.DiscountAmount, groupA.ShippingAmount, groupA.CommissionAmount, groupA.SellerPayable)
            .Should().Be((600_000m, 60_000m, 50_000m, 60_000m, 590_000m));
        (groupB.SubTotal, groupB.DiscountAmount, groupB.ShippingAmount, groupB.CommissionAmount, groupB.SellerPayable)
            .Should().Be((400_000m, 40_000m, 50_000m, 40_000m, 410_000m));
        (groupA.ShippingCostShare, groupB.ShippingCostShare).Should().Be((0m, 0m), "Batch 45: ارسال دیگر از فروشنده کسر نمی‌شود");

        var payoutA = await SinglePayoutAsync(seeded, 0);
        var payoutB = await SinglePayoutAsync(seeded, 1);

        // روز ۱: هنوز در پنجرهٔ مرجوعی.
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        SetCurrentUser(1, "Admin");
        var early = () => PayoutService.MarkAsPaidAsync(payoutA.SellerPayoutId, "EARLY");
        (await early.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYOUT_ON_HOLD");

        // روز ۸: فهرست ادمین خالص قابل واریز را نشان می‌دهد.
        Factory.Clock.Advance(TimeSpan.FromDays(7));
        var listed = (await PayoutService.GetAllPayoutsAsync()).ToDictionary(p => p.SellerPayoutId);
        (listed[payoutA.SellerPayoutId].Amount, listed[payoutA.SellerPayoutId].DebtOffsetAmount, listed[payoutA.SellerPayoutId].NetAmount)
            .Should().Be((590_000m, 100_000m, 490_000m));
        (listed[payoutB.SellerPayoutId].Amount, listed[payoutB.SellerPayoutId].DebtOffsetAmount, listed[payoutB.SellerPayoutId].NetAmount)
            .Should().Be((410_000m, 0m, 410_000m));

        var paidA = await PayoutService.MarkAsPaidAsync(payoutA.SellerPayoutId, "BANK-A", expectedNetAmount: 490_000m);
        var paidB = await PayoutService.MarkAsPaidAsync(payoutB.SellerPayoutId, "BANK-B", expectedNetAmount: 410_000m);

        paidA.NetAmount.Should().Be(490_000m);
        paidB.NetAmount.Should().Be(410_000m);
        // ۱٬۰۰۰٬۰۰۰ پرداختی مشتری = واریز به فروشندگان (۹۰۰٬۰۰۰) + بدهی وصول‌شده (۱۰۰٬۰۰۰) + کمیسیون (۱۰۰٬۰۰۰)
        // − هزینهٔ کوپن پلتفرم (۱۰۰٬۰۰۰). ارسال داخل سهم فروشندگان است (Batch 45).
        (paidA.NetAmount + paidB.NetAmount + paidA.DebtOffsetAmount + paidB.DebtOffsetAmount
            + groupA.CommissionAmount + groupB.CommissionAmount - order.DiscountAmount)
            .Should().Be(order.TotalAmount);
    }

    // ==================== helpers ====================

    private sealed record SeededOrder(int OrderId, int BuyerUserId, IReadOnlyList<(Seller Seller, int GroupId)> Groups);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private Task<SellerPayout> SinglePayoutAsync(SeededOrder seeded, int groupIndex)
    {
        var groupId = seeded.Groups[groupIndex].GroupId;
        return DbContext.SellerPayout.AsNoTracking().SingleAsync(p => p.OrderSellerGroupId == groupId);
    }

    private async Task<SellerDebt> AddDebtAsync(int sellerId, decimal amount)
    {
        var debt = new SellerDebt
        {
            SellerId = sellerId,
            OriginalAmount = amount,
            Reason = "B44 earlier clawback",
            CreatedAtUtc = ClockNow.AddDays(-30)
        };
        DbContext.SellerDebt.Add(debt);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return debt;
    }

    /// <summary>تسویهٔ دوره‌ای دستی که دوره‌اش دیروز بسته شده است (بدون نگه‌داشت).</summary>
    private async Task<int> AddAvailablePayoutAsync(int sellerId, decimal amount)
    {
        var payout = new SellerPayout(sellerId, null, amount, ClockNow.AddDays(-10), ClockNow.AddDays(-1));
        DbContext.SellerPayout.Add(payout);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payout.SellerPayoutId;
    }

    /// <summary>پرداخت واقعی (Initiate + Verify)، ارسال و تحویل همهٔ بسته‌ها با ساعت تست.</summary>
    private async Task PayShipDeliverAllAsync(SeededOrder seeded)
    {
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        (await PaymentService.VerifyAsync(initiation.Authority!)).State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();

        foreach (var (seller, groupId) in seeded.Groups)
        {
            SetCurrentUser(seller.UserId, "Seller");
            await DashboardService.ShipSellerGroupAsync(
                seeded.OrderId, groupId,
                new ShipSellerGroupRequest { CarrierName = "Tipax", TrackingNumber = $"TPX-{Guid.NewGuid():N}"[..20] });
            await DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);
            DbContext.ChangeTracker.Clear();
        }
    }

    private Task<SeededOrder> CreateOrderAsync(decimal prices) => CreateOrderAsync(new[] { prices });

    private async Task<SeededOrder> CreateOrderAsync(decimal[] prices, string? couponCode = null, int? shippingMethodId = null)
    {
        var listings = new List<(Seller Seller, int ListingId)>();
        foreach (var price in prices)
        {
            var (seller, _) = await CreateSellerAsync();
            listings.Add((seller, await CreateListingAsync(seller, price)));
        }

        var (buyerUser, _) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");
        foreach (var (_, listingId) in listings)
            await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingId, Quantity = 1 });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "B44 Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000",
            CouponCode = couponCode,
            ShippingMethodId = shippingMethodId
        });

        DbContext.ChangeTracker.Clear();
        var groups = await DbContext.OrderSellerGroup.AsNoTracking().Where(g => g.OrderId == order.OrderId).ToListAsync();

        return new SeededOrder(
            order.OrderId,
            buyerUser.UserId,
            listings.Select(l => (l.Seller, groups.Single(g => g.SellerId == l.Seller.SellerId).OrderSellerGroupId)).ToList());
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B44",
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

    private async Task<(Seller Seller, int UserId)> CreateSellerAsync()
    {
        var (sellerUser, _) = await CreateUserAsync();
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B44Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return (seller, sellerUser.UserId);
    }

    private async Task<int> CreateListingAsync(Seller seller, decimal price)
    {
        var product = new Product
        {
            Name = $"B44-{Guid.NewGuid():N}",
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
            RecipientFirstName = "B44",
            RecipientLastName = "Shop",
            UserId = seller.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, InitialStock));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        return listing.ListingId;
    }
}
