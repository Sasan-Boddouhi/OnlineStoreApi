using Application.Common.Helpers;
using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.Pricing;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

/// <summary>
/// Batch 45 (FIN-04، FIN-06، FIN-09، FIN-10، FIN-14؛ تصمیم‌های N4 تا N7 و اصلاحیهٔ مالک پس از Batch 44).
/// ناوردای پول برای هر سفارش: Σ سهم فروشنده + کمیسیون پلتفرم − هزینهٔ کوپن پلتفرم = پرداختی مشتری
/// (ارسال هر بسته داخل سهم فروشنده است). همهٔ مبالغ ریال کامل.
/// </summary>
public class PricingServiceTests
{
    private const decimal GatewayMinimum = 10_000m;

    private readonly PricingService _sut = new();

    private static PricingRequest Request(
        IEnumerable<(int SellerId, decimal SubTotal, decimal Rate)> groups,
        decimal shippingPerParcel = 0m,
        PricingCoupon? coupon = null,
        decimal minimum = GatewayMinimum)
        => new(groups.Select(g => new PricingGroupInput(g.SellerId, g.SubTotal, g.Rate)).ToList(), shippingPerParcel, coupon, minimum);

    private static PricingCoupon Platform(decimal percent, decimal? max = null)
        => new(percent, max, CouponFundingSource.Platform);

    // ==================== نمونهٔ مالک ====================

    [Fact]
    public void OwnerSampleOrder_CustomerPays950k_A565k_B385k_PlatformNetZero()
    {
        var result = _sut.Calculate(Request(
            new[] { (1, 600_000m, 10m), (2, 400_000m, 10m) },
            shippingPerParcel: 25_000m,
            coupon: Platform(10m)));

        result.ItemsTotal.Should().Be(1_000_000m);
        result.DiscountAmount.Should().Be(100_000m);
        result.ShippingTotal.Should().Be(50_000m, "۲۵٬۰۰۰ برای هر بسته (N4)");
        result.PayableTotal.Should().Be(950_000m);
        result.PlatformDiscountCost.Should().Be(100_000m, "کوپن پلتفرم هزینهٔ پلتفرم است");

        var a = result.Groups.Single(g => g.SellerId == 1);
        var b = result.Groups.Single(g => g.SellerId == 2);
        (a.DiscountAmount, a.PlatformDiscountShare, a.SellerFundedDiscount, a.ShippingAmount, a.CommissionAmount, a.SellerPayable)
            .Should().Be((60_000m, 60_000m, 0m, 25_000m, 60_000m, 565_000m));
        (b.DiscountAmount, b.PlatformDiscountShare, b.SellerFundedDiscount, b.ShippingAmount, b.CommissionAmount, b.SellerPayable)
            .Should().Be((40_000m, 40_000m, 0m, 25_000m, 40_000m, 385_000m));

        var commission = a.CommissionAmount + b.CommissionAmount;
        (commission - result.PlatformDiscountCost).Should().Be(0m, "خالص پلتفرم");
        (a.SellerPayable + b.SellerPayable + commission - result.PlatformDiscountCost).Should().Be(950_000m);
    }

    // ==================== ناوردا روی سفارش‌های تولیدشده ====================

    [Fact]
    public void Invariant_HoldsForEveryGeneratedOrder()
    {
        var random = new Random(4545);
        decimal[] rates = { 0m, 5m, 7.5m, 9.99m, 10m, 12.25m, 15m, 33.33m, 100m };
        decimal[] minimums = { 0m, 10_000m, 50_000m };

        for (var n = 0; n < 5_000; n++)
        {
            var groupCount = random.Next(1, 4);
            var sellerIds = Enumerable.Range(1, 50).OrderBy(_ => random.Next()).Take(groupCount).ToArray();
            var groups = sellerIds
                .Select(id => (id, (decimal)random.Next(1, 3_000_000), rates[random.Next(rates.Length)]))
                .ToArray();
            var shipping = random.Next(3) == 0 ? 0m : random.Next(0, 120_000);
            var minimum = minimums[random.Next(minimums.Length)];
            PricingCoupon? coupon = random.Next(4) == 0
                ? null
                : new PricingCoupon(
                    random.Next(1, 10_001) / 100m,
                    random.Next(3) == 0 ? random.Next(1, 500_000) + random.Next(0, 100) / 100m : null,
                    random.Next(5) == 0 ? CouponFundingSource.Seller : CouponFundingSource.Platform);

            var request = Request(groups, shipping, coupon, minimum);
            var result = _sut.Calculate(request);
            var because = $"order #{n}: {string.Join(", ", groups)} ship={shipping} coupon={coupon} min={minimum}";

            var all = new List<decimal> { result.DiscountAmount, result.PayableTotal, result.ShippingTotal, result.PlatformDiscountCost };
            all.AddRange(result.Groups.SelectMany(g => new[]
            {
                g.DiscountAmount, g.PlatformDiscountShare, g.SellerFundedDiscount, g.ShippingAmount, g.CommissionAmount, g.SellerPayable
            }));
            all.Should().OnlyContain(v => RialMoney.IsWhole(v) && v >= 0, because);

            (result.Groups.Sum(g => g.SellerPayable) + result.Groups.Sum(g => g.CommissionAmount) - result.PlatformDiscountCost)
                .Should().Be(result.PayableTotal, because);
            result.PayableTotal.Should().Be(result.ItemsTotal - result.DiscountAmount + result.ShippingTotal, because);
            result.Groups.Sum(g => g.DiscountAmount).Should().Be(result.DiscountAmount, because);
            result.Groups.Should().OnlyContain(g => g.ShippingAmount == shipping && g.DiscountAmount <= g.SubTotal, because);
            result.ShippingTotal.Should().Be(shipping * groupCount, because);

            if (coupon is null)
                result.DiscountAmount.Should().Be(0m, because);
            else if (coupon.MaxDiscountAmount is { } max)
                result.DiscountAmount.Should().BeLessThanOrEqualTo(max, because);

            // N7: کوپن هرگز مبلغ قابل پرداخت را زیر حداقل درگاه نمی‌برد (مگر سفارش بدون کوپن هم زیر آن باشد).
            if (result.DiscountAmount > 0)
                result.PayableTotal.Should().BeGreaterThanOrEqualTo(minimum, because);

            // N5: کوپن پلتفرم ⇒ کمیسیون روی قیمت پیش از کوپن و سهم فروشنده بدون کسر کوپن.
            foreach (var g in result.Groups)
            {
                if (coupon?.FundedBy != CouponFundingSource.Seller)
                {
                    g.PlatformDiscountShare.Should().Be(g.DiscountAmount, because);
                    g.CommissionAmount.Should().Be(RialMoney.Round(g.SubTotal * g.CommissionRate / 100m), because);
                    g.SellerPayable.Should().Be(g.SubTotal - g.CommissionAmount + g.ShippingAmount, because);
                }
                else
                {
                    g.PlatformDiscountShare.Should().Be(0m, because);
                    g.CommissionAmount.Should().Be(RialMoney.Round((g.SubTotal - g.DiscountAmount) * g.CommissionRate / 100m), because);
                }
            }
        }
    }

    // ==================== گرد کردن و باقیمانده (N6) ====================

    [Fact]
    public void SevenPercentOn99999_IsRoundedToAWholeRial()
    {
        var result = _sut.Calculate(Request(new[] { (1, 99_999m, 10m) }, coupon: Platform(7m)));

        result.DiscountAmount.Should().Be(7_000m, "6,999.93 rounds to 7,000");
        result.PayableTotal.Should().Be(92_999m);
    }

    [Fact]
    public void Midpoint_RoundsHalfAwayFromZero()
    {
        // ۲۵ × ۱۰٪ = ۲٫۵ ⇒ ۳ (گرد بانکی ۲ می‌داد؛ پیش از Batch 45 تخفیف با گرد بانکی حساب می‌شد).
        _sut.Calculate(Request(new[] { (1, 25m, 10m) }, coupon: Platform(10m), minimum: 0m))
            .DiscountAmount.Should().Be(3m);
        _sut.Calculate(Request(new[] { (1, 25m, 10m) }, minimum: 0m))
            .Groups.Single().CommissionAmount.Should().Be(3m);
    }

    /// <summary>
    /// Batch 45b: در تساوی جمع اقلام، باقیمانده روی بزرگ‌ترین شناسهٔ فروشنده می‌نشیند (ترتیب قطعی؛ همان نتیجهٔ Batch 45).
    /// </summary>
    [Fact]
    public void SplitRemainder_TiedLargestGroups_GoesToTheHighestSellerId()
    {
        var result = _sut.Calculate(Request(
            new[] { (30, 100_000m, 10m), (10, 100_000m, 10m), (20, 100_000m, 10m) },
            coupon: Platform(50m, max: 10_001m)));

        result.DiscountAmount.Should().Be(10_001m);
        result.Groups.Select(g => g.SellerId).Should().Equal(10, 20, 30);
        result.Groups.Select(g => g.DiscountAmount).Should().Equal(3_334m, 3_334m, 3_333m);
    }

    /// <summary>
    /// Batch 45b (درخواست مالک): باقیماندهٔ گرد کردن به بزرگ‌ترین گروه می‌رسد، نه گروه آخر.
    /// سهم‌های دقیق ۱٬۶۶۶٫۸۳ / ۵٬۰۰۰٫۵۰ / ۳٬۳۳۳٫۶۷ ⇒ ۱٬۶۶۷ و ۳٬۳۳۴ گرد می‌شوند و بزرگ‌ترین (فروشندهٔ ۲۰) باقیماندهٔ ۵٬۰۰۰ را می‌گیرد.
    /// پیش‌تر: ۱٬۶۶۷ / ۵٬۰۰۱ / ۳٬۳۳۳ (باقیمانده روی گروه آخر، فروشندهٔ ۳۰).
    /// </summary>
    [Fact]
    public void SplitRemainder_GoesToTheLargestGroup_NotTheLast()
    {
        var result = _sut.Calculate(Request(
            new[] { (10, 100_000m, 10m), (20, 300_000m, 10m), (30, 200_000m, 10m) },
            coupon: Platform(50m, max: 10_001m)));

        result.DiscountAmount.Should().Be(10_001m);
        result.Groups.Select(g => g.SellerId).Should().Equal(10, 20, 30);
        result.Groups.Select(g => g.DiscountAmount).Should().Equal(1_667m, 5_000m, 3_334m);
    }

    /// <summary>
    /// Batch 45b: کوپن ۷٪ روی اقلامی به جمع ۱۲۳٬۴۵۷ (۸٬۶۴۱٫۹۹ ⇒ ۸٬۶۴۲). سهم‌های دقیق ۴٬۳۲۱٫۰۴ / ۲٬۱۶۰٫۴۸ / ۲٬۱۶۰٫۴۸؛
    /// دو گروه کوچک ۲٬۱۶۰ می‌گیرند و بزرگ‌ترین گروه (فروشندهٔ ۱، نه آخرین) باقیماندهٔ ۴٬۳۲۲ را.
    /// همهٔ مبالغ ریال کامل و ناوردا برقرار. پیش‌تر: ۴٬۳۲۱ / ۲٬۱۶۰ / ۲٬۱۶۱.
    /// </summary>
    [Fact]
    public void SevenPercentCouponOn123457_AllAmountsWhole_InvariantHolds_RemainderOnLargestGroup()
    {
        var result = _sut.Calculate(Request(
            new[] { (1, 61_729m, 10m), (2, 30_864m, 10m), (3, 30_864m, 10m) },
            shippingPerParcel: 25_000m,
            coupon: Platform(7m)));

        result.ItemsTotal.Should().Be(123_457m);
        result.DiscountAmount.Should().Be(8_642m, "8,641.99 rounds half away from zero to 8,642");
        result.Groups.Select(g => g.DiscountAmount).Should().Equal(4_322m, 2_160m, 2_160m);
        result.Groups.Select(g => g.CommissionAmount).Should().Equal(6_173m, 3_086m, 3_086m);
        result.Groups.Select(g => g.SellerPayable).Should().Equal(80_556m, 52_778m, 52_778m);
        result.PayableTotal.Should().Be(123_457m - 8_642m + 75_000m);

        var all = new List<decimal> { result.DiscountAmount, result.PayableTotal, result.ShippingTotal, result.PlatformDiscountCost };
        all.AddRange(result.Groups.SelectMany(g => new[]
        {
            g.DiscountAmount, g.PlatformDiscountShare, g.SellerFundedDiscount, g.ShippingAmount, g.CommissionAmount, g.SellerPayable
        }));
        all.Should().OnlyContain(v => RialMoney.IsWhole(v) && v >= 0);

        result.Groups.Sum(g => g.DiscountAmount).Should().Be(result.DiscountAmount);
        (result.Groups.Sum(g => g.SellerPayable) + result.Groups.Sum(g => g.CommissionAmount) - result.PlatformDiscountCost)
            .Should().Be(result.PayableTotal);
    }

    // ==================== مقدار کسری × قیمت (Batch 45b) ====================

    [Theory]
    [InlineData(0.5, 12_345, 6_173)]      // 6,172.5 ⇒ 6,173 (نیمه به دور از صفر)
    [InlineData(1.5, 24_691, 37_037)]     // 37,036.5 ⇒ 37,037
    [InlineData(0.33, 10_000, 3_300)]
    [InlineData(2.25, 99_999, 224_998)]   // 224,997.75 ⇒ 224,998
    [InlineData(3, 12_345, 37_035)]
    public void LineTotal_FractionalQuantity_IsRoundedToAWholeRial(double quantity, int unitPrice, int expected)
    {
        RialMoney.LineTotal((decimal)quantity, unitPrice).Should().Be(expected);
    }

    [Fact]
    public void OrderItem_FractionalQuantity_HasAWholeRialTotal_AndTheOrderTotalMatches()
    {
        var item = new OrderItem(productId: 1, quantity: 0.5m, unitPrice: 12_345m);
        item.TotalPrice.Should().Be(6_173m);

        item.UpdateQuantity(1.5m);
        item.TotalPrice.Should().Be(18_518m, "18,517.5 ⇒ 18,518");

        var order = new Order();
        order.AddItem(item);
        order.TotalAmount.Should().Be(18_518m);

        var act = () => order.ApplyDiscountsAndShipping(0m, 0m, null, null);
        act.Should().NotThrow();
        order.TotalAmount.Should().Be(item.TotalPrice);
    }

    [Fact]
    public void CartItem_FractionalQuantity_HasAWholeRialLineTotal()
    {
        var cart = new Cart();
        cart.Items.Add(new CartItem { ListingId = 1, Quantity = 0.5m, UnitPrice = 12_345m });
        cart.Items.Add(new CartItem { ListingId = 2, Quantity = 0.5m, UnitPrice = 12_345m });

        cart.Items.Should().OnlyContain(i => i.LineTotal == 6_173m);
        cart.TotalAmount.Should().Be(12_346m, "each line is rounded on its own, the same way the order rounds it");
    }

    [Fact]
    public void MaxDiscountAmount_WithAFraction_IsFloored()
    {
        _sut.Calculate(Request(new[] { (1, 1_000_000m, 10m) }, coupon: Platform(50m, max: 12_345.67m)))
            .DiscountAmount.Should().Be(12_345m);
    }

    // ==================== سقف N7 ====================

    [Fact]
    public void HundredPercentCoupon_IsCappedAtTheGatewayMinimum()
    {
        var result = _sut.Calculate(Request(new[] { (1, 50_000m, 10m) }, coupon: Platform(100m)));

        result.DiscountAmount.Should().Be(40_000m);
        result.PayableTotal.Should().Be(10_000m);
        result.DiscountCappedByMinimumPayable.Should().BeTrue();
        result.Groups.Single().SellerPayable.Should().Be(45_000m, "the platform pays the coupon; the seller keeps 50,000 − 5,000 commission");
    }

    [Fact]
    public void HundredPercentCoupon_WithShipping_StillLeavesTheShippingToPay()
    {
        var result = _sut.Calculate(Request(new[] { (1, 50_000m, 10m) }, shippingPerParcel: 25_000m, coupon: Platform(100m)));

        result.DiscountAmount.Should().Be(50_000m, "the coupon never exceeds the items total");
        result.PayableTotal.Should().Be(25_000m);
        result.DiscountCappedByMinimumPayable.Should().BeFalse();
    }

    [Fact]
    public void OrderAlreadyBelowTheMinimum_GetsNoDiscount()
    {
        var result = _sut.Calculate(Request(new[] { (1, 8_000m, 10m) }, coupon: Platform(10m)));

        result.DiscountAmount.Should().Be(0m);
        result.PayableTotal.Should().Be(8_000m);
        result.DiscountCappedByMinimumPayable.Should().BeTrue();
    }

    // ==================== ارسال هر بسته (N4) ====================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Shipping_IsChargedPerParcel_AndCreditedToEachShipper(int sellers)
    {
        var groups = Enumerable.Range(1, sellers).Select(i => (i, 100_000m, 10m));

        var result = _sut.Calculate(Request(groups, shippingPerParcel: 30_000m));

        result.ShippingTotal.Should().Be(30_000m * sellers);
        result.PayableTotal.Should().Be(100_000m * sellers + 30_000m * sellers);
        result.Groups.Should().OnlyContain(g => g.ShippingAmount == 30_000m && g.SellerPayable == 100_000m - 10_000m + 30_000m);
    }

    // ==================== کوپن فروشنده (N5، آینده) ====================

    [Fact]
    public void SellerFundedCoupon_IsDeductedFromTheSeller_AndCommissionIsAfterTheDiscount()
    {
        var result = _sut.Calculate(Request(
            new[] { (1, 600_000m, 10m), (2, 400_000m, 10m) },
            shippingPerParcel: 25_000m,
            coupon: new PricingCoupon(10m, null, CouponFundingSource.Seller)));

        result.PlatformDiscountCost.Should().Be(0m);
        var a = result.Groups.Single(g => g.SellerId == 1);
        (a.SellerFundedDiscount, a.CommissionAmount, a.SellerPayable).Should().Be((60_000m, 54_000m, 511_000m));
        (result.Groups.Sum(g => g.SellerPayable) + result.Groups.Sum(g => g.CommissionAmount)).Should().Be(result.PayableTotal);
    }

    // ==================== ورودی نامعتبر ====================

    [Fact]
    public void FractionalSubTotal_IsRefused()
    {
        var act = () => _sut.Calculate(Request(new[] { (1, 1_000.5m, 10m) }));

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be(RialMoney.NotWholeErrorCode);
    }

    [Fact]
    public void NoGroups_IsRefused()
    {
        var act = () => _sut.Calculate(Request(Array.Empty<(int, decimal, decimal)>()));

        act.Should().Throw<ArgumentException>();
    }

    // ==================== موجودیت گروه با همان قاعده ====================

    [Fact]
    public void OrderSellerGroup_ApplyPricing_MatchesThePricingService()
    {
        var group = new OrderSellerGroup(sellerId: 1, commissionRate: 10m);
        typeof(OrderSellerGroup).GetProperty(nameof(OrderSellerGroup.SubTotal))!.SetValue(group, 600_000m);

        group.ApplyPricing(discountAmount: 60_000m, platformDiscountShare: 60_000m, shippingAmount: 25_000m);
        group.ApplyCommission();

        (group.CommissionAmount, group.SellerPayable, group.ShippingAmount, group.PlatformDiscountShare, group.ShippingCostShare)
            .Should().Be((60_000m, 565_000m, 25_000m, 60_000m, 0m));
    }

    [Fact]
    public void OrderSellerGroup_ApplyPricing_RefusesFractions()
    {
        var group = new OrderSellerGroup(sellerId: 1, commissionRate: 10m);
        typeof(OrderSellerGroup).GetProperty(nameof(OrderSellerGroup.SubTotal))!.SetValue(group, 1_000m);

        var act = () => group.ApplyPricing(100.5m, 100.5m, 0m);

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("PRICING_INVARIANT_VIOLATED");
    }

    [Fact]
    public void SellerDebt_RemainingAmount_FollowsOriginalAndSettlements()
    {
        var debt = new SellerDebt { SellerId = 1, OriginalAmount = 1_500m, Reason = "test" };
        debt.RemainingAmount.Should().Be(1_500m);

        debt.ApplySettlement(1_000m, DateTime.UtcNow);
        (debt.SettledAmount, debt.RemainingAmount, debt.IsSettled).Should().Be((1_000m, 500m, false));

        debt.ApplySettlement(500m, DateTime.UtcNow);
        (debt.SettledAmount, debt.RemainingAmount, debt.IsSettled).Should().Be((1_500m, 0m, true));
    }
}
