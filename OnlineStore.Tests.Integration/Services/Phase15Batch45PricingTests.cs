using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 45 (FIN-04، FIN-06، FIN-09، FIN-10، FIN-14؛ تصمیم‌های N4 تا N7 و اصلاحیهٔ مالک پس از Batch 44).
///  • کوپن پلتفرم هزینهٔ پلتفرم است و از سهم فروشنده کم نمی‌شود؛ کمیسیون روی قیمت اقلام پیش از کوپن است.
///  • ارسال برای هر بسته (هر فروشنده) جدا گرفته می‌شود و به سهم ارسال‌کننده (فروشنده) افزوده می‌شود.
///  • همهٔ مبالغ ریال کامل‌اند؛ کوپن طوری سقف می‌خورد که مبلغ قابل پرداخت زیر حداقل درگاه (۱۰٬۰۰۰ ریال) نرود.
///  • بدهی فروشنده ستون صریح <c>RemainingAmount</c> دارد و کسر بدهی از روی آن است.
/// برگشت جزئی (یک بسته یا یک قلم) کار Batch 64 است و این‌جا آزموده نمی‌شود.
/// </summary>
public class Phase15Batch45PricingTests : ControllerIntegrationTestBase
{
    private const decimal InitialStock = 10m;

    private ICartService CartService => GetService<ICartService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private ISellerPayoutService PayoutService => GetService<ISellerPayoutService>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private DateTime ClockNow => Factory.Clock.GetUtcNow().UtcDateTime;

    public Phase15Batch45PricingTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// نمونهٔ مالک: A ۶۰۰٬۰۰۰، B ۴۰۰٬۰۰۰، کوپن پلتفرم ۱۰٪ (۱۰۰٬۰۰۰)، ارسال ۲۵٬۰۰۰ برای هر بسته، کمیسیون ۱۰٪،
    /// و بدهی ۱۰۰٬۰۰۰ فروشندهٔ A. مشتری ۹۵۰٬۰۰۰ می‌پردازد؛ سهم A ۵۶۵٬۰۰۰ و B ۳۸۵٬۰۰۰؛ خالص پلتفرم ۰.
    /// پیش از Batch 45: ارسال یک بار (۲۵٬۰۰۰)، جمع ۹۲۵٬۰۰۰ و سهم‌ها ۴۸۷٬۵۰۰ / ۳۲۷٬۵۰۰ بود.
    /// </summary>
    [Fact]
    public async Task SampleOrder_PlatformCoupon_ShippingPerParcel_RialByRial_ThroughHoldAndDebtNetting()
    {
        var shipping = await AddShippingMethodAsync(25_000m);
        var coupon = await AddCouponAsync(10m);

        var seeded = await CreateOrderAsync(new[] { 600_000m, 400_000m }, coupon.Code, shipping.ShippingMethodId);
        var sellerA = seeded.Groups[0].Seller;
        var sellerB = seeded.Groups[1].Seller;

        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        order.DiscountAmount.Should().Be(100_000m, "کوپن ۱۰٪ روی ۱٬۰۰۰٬۰۰۰");
        order.ShippingCost.Should().Be(50_000m, "۲ بسته × ۲۵٬۰۰۰ (N4)");
        order.TotalAmount.Should().Be(950_000m, "۱٬۰۰۰٬۰۰۰ − ۱۰۰٬۰۰۰ + ۵۰٬۰۰۰");

        await AddDebtAsync(sellerA.SellerId, 100_000m);
        await PayShipDeliverAllAsync(seeded);

        var groups = await DbContext.OrderSellerGroup.AsNoTracking().Where(g => g.OrderId == seeded.OrderId).ToListAsync();
        var groupA = groups.Single(g => g.SellerId == sellerA.SellerId);
        var groupB = groups.Single(g => g.SellerId == sellerB.SellerId);

        // کمیسیون روی قیمت پیش از کوپن (N5)؛ سهم = اقلام − کمیسیون + ارسال بستهٔ خودش؛ کوپن پلتفرم کم نمی‌شود.
        (groupA.SubTotal, groupA.DiscountAmount, groupA.CommissionAmount, groupA.SellerPayable)
            .Should().Be((600_000m, 60_000m, 60_000m, 565_000m));
        (groupB.SubTotal, groupB.DiscountAmount, groupB.CommissionAmount, groupB.SellerPayable)
            .Should().Be((400_000m, 40_000m, 40_000m, 385_000m));
        (groupA.ShippingAmount, groupA.PlatformDiscountShare, groupA.ShippingCostShare).Should().Be((25_000m, 60_000m, 0m));
        (groupB.ShippingAmount, groupB.PlatformDiscountShare, groupB.ShippingCostShare).Should().Be((25_000m, 40_000m, 0m));

        var commission = groupA.CommissionAmount + groupB.CommissionAmount;
        var platformCouponCost = order.DiscountAmount;
        (commission - platformCouponCost).Should().Be(0m, "خالص پلتفرم = کمیسیون ۱۰۰٬۰۰۰ − هزینهٔ کوپن ۱۰۰٬۰۰۰");
        (groupA.SellerPayable + groupB.SellerPayable + commission - platformCouponCost)
            .Should().Be(order.TotalAmount, "Σ سهم فروشنده + کمیسیون − هزینهٔ کوپن پلتفرم = پرداختی مشتری");

        var payoutA = await SinglePayoutAsync(seeded, 0);
        var payoutB = await SinglePayoutAsync(seeded, 1);
        payoutA.Amount.Should().Be(565_000m);
        payoutB.Amount.Should().Be(385_000m);

        // روز ۱: هنوز در نگه‌داشت ۷ روزه.
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        SetCurrentUser(1, "Admin");
        var early = () => PayoutService.MarkAsPaidAsync(payoutA.SellerPayoutId, "EARLY");
        (await early.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYOUT_ON_HOLD");

        // روز ۸: بدهی ۱۰۰٬۰۰۰ از تسویهٔ A کسر می‌شود.
        Factory.Clock.Advance(TimeSpan.FromDays(7));
        var paidA = await PayoutService.MarkAsPaidAsync(payoutA.SellerPayoutId, "BANK-A", expectedNetAmount: 465_000m);
        var paidB = await PayoutService.MarkAsPaidAsync(payoutB.SellerPayoutId, "BANK-B", expectedNetAmount: 385_000m);

        (paidA.Amount, paidA.DebtOffsetAmount, paidA.NetAmount).Should().Be((565_000m, 100_000m, 465_000m));
        (paidB.Amount, paidB.DebtOffsetAmount, paidB.NetAmount).Should().Be((385_000m, 0m, 385_000m));

        // ۹۵۰٬۰۰۰ = واریز ۸۵۰٬۰۰۰ + بدهی وصول‌شده ۱۰۰٬۰۰۰ + کمیسیون ۱۰۰٬۰۰۰ − هزینهٔ کوپن پلتفرم ۱۰۰٬۰۰۰.
        (paidA.NetAmount + paidB.NetAmount + paidA.DebtOffsetAmount + paidB.DebtOffsetAmount + commission - platformCouponCost)
            .Should().Be(order.TotalAmount);
    }

    /// <summary>FIN-06: کوپن ۷٪ روی ۹۹٬۹۹۹ = ۶٬۹۹۹٫۹۳ → ۶٬۹۹۹ ریال کامل نیست؛ اکنون ۷٬۰۰۰ (نیمه به دور از صفر).</summary>
    [Fact]
    public async Task SevenPercentCouponOn99999_OrderTotalIsAWholeRial()
    {
        var coupon = await AddCouponAsync(7m);

        var seeded = await CreateOrderAsync(new[] { 99_999m }, coupon.Code, shippingMethodId: null);

        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        order.DiscountAmount.Should().Be(7_000m);
        order.TotalAmount.Should().Be(92_999m);
        order.TotalAmount.Should().Be(decimal.Truncate(order.TotalAmount), "every amount is a whole rial (N6)");

        var group = await DbContext.OrderSellerGroup.AsNoTracking().SingleAsync(g => g.OrderId == seeded.OrderId);
        group.DiscountAmount.Should().Be(7_000m);
        group.SellerPayable.Should().Be(decimal.Truncate(group.SellerPayable));
    }

    /// <summary>FIN-09 / N7: کوپن ۱۰۰٪ سفارشی با مبلغ صفر می‌ساخت که هرگز پرداخت نمی‌شد؛ اکنون تا حداقل درگاه سقف می‌خورد.</summary>
    [Fact]
    public async Task CouponThatWouldZeroTheOrder_IsCappedSoThePayableStaysAtTheGatewayMinimum()
    {
        var coupon = await AddCouponAsync(100m);

        var seeded = await CreateOrderAsync(new[] { 50_000m }, coupon.Code, shippingMethodId: null);

        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        order.TotalAmount.Should().Be(10_000m, "PaymentGateway:MinimumAmountRial (۱۰٬۰۰۰ ریال) کف مبلغ قابل پرداخت است");
        order.DiscountAmount.Should().Be(40_000m);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        initiation.Amount.Should().Be(10_000m, "the capped order can actually be paid");
    }

    /// <summary>
    /// Batch 45b: مقدار کسری مجاز است (کالای کیلویی/متری). ۱٫۵ × ۲۴٬۶۹۱ = ۳۷٬۰۳۶٫۵ پیش‌تر جمع گروه را کسری می‌کرد و
    /// checkout با 409 AMOUNT_NOT_WHOLE_RIAL شکست می‌خورد. اکنون جمع هر قلم به ریال کامل گرد می‌شود (نیمه به دور از صفر، N6):
    /// ۳۷٬۰۳۷؛ تخفیف ۷٪ = ۲٬۵۹۲٫۵۹ ⇒ ۲٬۵۹۳؛ با ارسال ۲۵٬۰۰۰ مشتری ۵۹٬۴۴۴ می‌پردازد.
    /// </summary>
    [Fact]
    public async Task FractionalQuantity_LineTotalIsAWholeRial_OrderChecksOutAndCanBePaid()
    {
        var shipping = await AddShippingMethodAsync(25_000m);
        var coupon = await AddCouponAsync(7m);

        var seeded = await CreateOrderAsync(new[] { 24_691m }, coupon.Code, shipping.ShippingMethodId, quantity: 1.5m);

        var item = await DbContext.OrderItem.AsNoTracking().SingleAsync(i => i.OrderId == seeded.OrderId);
        item.TotalPrice.Should().Be(37_037m);

        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        (order.DiscountAmount, order.ShippingCost, order.TotalAmount).Should().Be((2_593m, 25_000m, 59_444m));

        var group = await DbContext.OrderSellerGroup.AsNoTracking().SingleAsync(g => g.OrderId == seeded.OrderId);
        group.SubTotal.Should().Be(37_037m);
        new[] { group.DiscountAmount, group.CommissionAmount, group.SellerPayable }
            .Should().OnlyContain(v => v == decimal.Truncate(v));

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        initiation.Amount.Should().Be(59_444m);
    }

    /// <summary>
    /// اصلاحیهٔ مالک ۲: باقی‌ماندهٔ بدهی یک ستون واقعی است (نه مقدار محاسبه‌شده یا حدس «تغییر بعد از ساخت»)،
    /// پس در SQL قابل فیلتر است و کسر بدهی از روی همان انجام می‌شود.
    /// </summary>
    [Fact]
    public async Task SellerDebtRemainingAmount_IsAStoredColumn_UpdatedBySettlement_AndUsedForNetting()
    {
        var (seller, _) = await CreateSellerAsync();
        var debt = await AddDebtAsync(seller.SellerId, 1_500m);
        var first = await AddAvailablePayoutAsync(seller.SellerId, 1_000m);

        SetCurrentUser(1, "Admin");
        (await PayoutService.MarkAsPaidAsync(first, "BANK-R1")).NetAmount.Should().Be(0m);

        DbContext.ChangeTracker.Clear();
        var openIds = await DbContext.SellerDebt.AsNoTracking()
            .Where(d => d.SellerId == seller.SellerId && d.RemainingAmount == 500m)
            .Select(d => d.SellerDebtId)
            .ToListAsync();
        openIds.Should().ContainSingle().Which.Should().Be(debt.SellerDebtId);

        var second = await AddAvailablePayoutAsync(seller.SellerId, 800m);
        var paid = await PayoutService.MarkAsPaidAsync(second, "BANK-R2", expectedNetAmount: 300m);
        paid.DebtOffsetAmount.Should().Be(500m);

        DbContext.ChangeTracker.Clear();
        (await DbContext.SellerDebt.AsNoTracking()
                .CountAsync(d => d.SellerId == seller.SellerId && d.RemainingAmount == 0m && d.IsSettled))
            .Should().Be(1);
    }

    // ==================== helpers ====================

    private sealed record SeededOrder(int OrderId, int BuyerUserId, IReadOnlyList<(Seller Seller, int GroupId)> Groups);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private async Task<ShippingMethod> AddShippingMethodAsync(decimal baseCost)
    {
        var shipping = new ShippingMethod { Name = "Post", Slug = $"post-{Guid.NewGuid():N}"[..20], BaseCost = baseCost, IsActive = true };
        DbContext.ShippingMethod.Add(shipping);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return shipping;
    }

    private async Task<Coupon> AddCouponAsync(decimal percentage)
    {
        var coupon = new Coupon { Code = $"B45{Guid.NewGuid():N}"[..12].ToUpperInvariant(), DiscountPercentage = percentage, IsActive = true };
        DbContext.Coupon.Add(coupon);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return coupon;
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
            Reason = "B45 earlier clawback",
            CreatedAtUtc = ClockNow.AddDays(-30)
        };
        DbContext.SellerDebt.Add(debt);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return debt;
    }

    private async Task<int> AddAvailablePayoutAsync(int sellerId, decimal amount)
    {
        var payout = new SellerPayout(sellerId, null, amount, ClockNow.AddDays(-10), ClockNow.AddDays(-1));
        DbContext.SellerPayout.Add(payout);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payout.SellerPayoutId;
    }

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

    private async Task<SeededOrder> CreateOrderAsync(decimal[] prices, string? couponCode, int? shippingMethodId, decimal quantity = 1m)
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
            await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingId, Quantity = quantity });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "B45 Buyer",
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
            FirstName = "B45",
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
            StoreName = $"B45Shop-{Guid.NewGuid():N}",
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
            Name = $"B45-{Guid.NewGuid():N}",
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
            RecipientFirstName = "B45",
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
