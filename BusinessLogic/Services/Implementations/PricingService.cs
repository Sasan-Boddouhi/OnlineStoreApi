using Application.Common.Helpers;
using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.Pricing;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 45 — مدل پول یک سفارش (تصمیم‌های N4 تا N7). خالص و بدون دیتابیس.
/// <list type="bullet">
/// <item>همهٔ مبالغ ریال کامل‌اند؛ تخفیف و کمیسیون با «نیمه به دور از صفر» گرد می‌شوند (<see cref="RialMoney"/>).</item>
/// <item>ارسال: هر گروه فروشنده یک بسته است و هزینهٔ پایهٔ روش ارسال را جدا می‌گیرد؛ مشتری جمع را می‌پردازد و
/// هزینهٔ هر بسته به ارسال‌کنندهٔ همان بسته (فروشنده) بستانکار می‌شود — داخل سهم فروشنده است و هرگز کسر نمی‌شود
/// (N4، FIN-04، FIN-10).</item>
/// <item>تخفیف: درصد روی جمع اقلام، سپس سقف کوپن، سپس سقفی که مبلغ قابل پرداخت را در حداقل درگاه نگه می‌دارد (N7، FIN-09).
/// سهم هر گروه به نسبت جمع اقلامش؛ باقیماندهٔ گرد کردن روی گروه آخر (به ترتیب شناسهٔ فروشنده).</item>
/// <item>کمیسیون: کوپن پلتفرم ⇒ روی جمع اقلام پیش از تخفیف و تخفیف هزینهٔ پلتفرم است؛ کوپن فروشنده ⇒ روی مبلغ پس از
/// تخفیف و تخفیف از سهم فروشنده کم می‌شود (N5، FIN-14).</item>
/// </list>
/// ناوردا (ارسال داخل سهم فروشنده است): Σ سهم فروشنده + Σ کمیسیون − هزینهٔ کوپن پلتفرم = مبلغ قابل پرداخت.
/// </summary>
public sealed class PricingService : IPricingService
{
    public PricingResult Calculate(PricingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Groups is null || request.Groups.Count == 0)
            throw new ArgumentException("A priced order needs at least one seller group.", nameof(request));

        // ترتیب قطعی: باقیماندهٔ تقسیم همیشه روی گروه با بزرگ‌ترین شناسهٔ فروشنده می‌نشیند.
        var groups = request.Groups.OrderBy(g => g.SellerId).ToList();

        foreach (var group in groups)
        {
            if (group.SubTotal < 0)
                throw new ArgumentOutOfRangeException(nameof(request), "A group sub-total cannot be negative.");
            if (group.CommissionRate < 0 || group.CommissionRate > 100)
                throw new ArgumentOutOfRangeException(nameof(request), "Commission rate must be between 0 and 100.");
            EnsureWhole(group.SubTotal);
        }

        if (request.ShippingFeePerParcel < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Shipping fee cannot be negative.");
        EnsureWhole(request.ShippingFeePerParcel);

        var itemsTotal = groups.Sum(g => g.SubTotal);
        var shippingTotal = request.ShippingFeePerParcel * groups.Count;

        var (discount, cappedByMinimum) = CalculateDiscount(request, itemsTotal, shippingTotal);
        var shares = SplitDiscount(discount, groups);
        var platformFunded = request.Coupon?.FundedBy != CouponFundingSource.Seller;

        var priced = new List<SellerGroupPricing>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var platformShare = platformFunded ? shares[i] : 0m;
            var sellerFunded = shares[i] - platformShare;
            var commission = RialMoney.Commission(group.SubTotal - sellerFunded, group.CommissionRate);
            var payable = RialMoney.SellerPayable(group.SubTotal, sellerFunded, commission, request.ShippingFeePerParcel);

            priced.Add(new SellerGroupPricing(
                group.SellerId,
                group.SubTotal,
                group.CommissionRate,
                shares[i],
                platformShare,
                sellerFunded,
                request.ShippingFeePerParcel,
                commission,
                payable));
        }

        var payableTotal = itemsTotal - discount + shippingTotal;
        var platformCost = priced.Sum(g => g.PlatformDiscountShare);

        // ناوردای پول: اگر روزی یک تغییر آن را بشکند، سفارش ثبت نمی‌شود (بهتر از ثبت پول ناجور).
        var settled = priced.Sum(g => g.SellerPayable) + priced.Sum(g => g.CommissionAmount) - platformCost;
        if (settled != payableTotal || priced.Sum(g => g.DiscountAmount) != discount
            || priced.Any(g => g.SellerPayable < 0 || g.DiscountAmount > g.SubTotal))
            throw new InvalidOperationException(
                $"Pricing invariant violated: settled {settled} != payable {payableTotal}.");

        return new PricingResult(
            itemsTotal,
            discount,
            cappedByMinimum,
            shippingTotal,
            payableTotal,
            platformCost,
            priced);
    }

    /// <summary>
    /// تخفیف کوپن: درصد روی جمع اقلام (گرد به ریال کامل)، سقف کوپن (کف ریال)، حداکثر برابر جمع اقلام،
    /// و در پایان سقف N7: مبلغ قابل پرداخت هرگز زیر حداقل درگاه نمی‌رود.
    /// </summary>
    private static (decimal Discount, bool CappedByMinimum) CalculateDiscount(
        PricingRequest request, decimal itemsTotal, decimal shippingTotal)
    {
        var coupon = request.Coupon;
        if (coupon is null || coupon.DiscountPercentage <= 0 || itemsTotal <= 0)
            return (0m, false);

        var percentage = Math.Min(coupon.DiscountPercentage, 100m);
        var discount = RialMoney.Round(itemsTotal * percentage / 100m);

        if (coupon.MaxDiscountAmount.HasValue)
            discount = Math.Min(discount, Math.Max(0m, decimal.Floor(coupon.MaxDiscountAmount.Value)));

        discount = Math.Min(discount, itemsTotal);

        // حداقل درگاه به بالا گرد می‌شود تا مبلغ پرداختی هرگز زیر آن نباشد.
        var minimum = Math.Max(0m, decimal.Ceiling(request.MinimumPayableAmount));
        var allowed = Math.Max(0m, itemsTotal + shippingTotal - minimum);
        if (discount > allowed)
            return (allowed, true);

        return (discount, false);
    }

    /// <summary>
    /// تقسیم تخفیف بین گروه‌ها به نسبت جمع اقلام، هر سهم گرد به ریال کامل و باقیمانده روی گروه آخر.
    /// هیچ سهمی از جمع اقلام گروه خودش بیشتر نمی‌شود؛ اگر باقیماندهٔ گروه آخر از جمع اقلامش بیشتر شود
    /// (فقط در تخفیف نزدیک ۱۰۰٪ ممکن است)، اضافه به گروه‌های قبلی که جا دارند برمی‌گردد.
    /// </summary>
    private static decimal[] SplitDiscount(decimal discount, IReadOnlyList<PricingGroupInput> groups)
    {
        var shares = new decimal[groups.Count];
        if (discount <= 0)
            return shares;

        var itemsTotal = groups.Sum(g => g.SubTotal);
        var last = groups.Count - 1;
        var allocated = 0m;

        for (var i = 0; i < last; i++)
        {
            var share = RialMoney.Round(discount * groups[i].SubTotal / itemsTotal);
            share = Math.Min(share, Math.Min(groups[i].SubTotal, discount - allocated));
            shares[i] = share;
            allocated += share;
        }

        var remainder = discount - allocated;
        var overflow = remainder - groups[last].SubTotal;
        if (overflow > 0)
        {
            remainder = groups[last].SubTotal;
            for (var j = last - 1; j >= 0 && overflow > 0; j--)
            {
                var take = Math.Min(groups[j].SubTotal - shares[j], overflow);
                shares[j] += take;
                overflow -= take;
            }
        }

        shares[last] = remainder;
        return shares;
    }

    private static void EnsureWhole(decimal amount)
    {
        if (!RialMoney.IsWhole(amount))
            throw new BusinessRuleViolationException(
                "مبالغ سفارش باید ریال کامل باشند؛ قیمت یا هزینهٔ ارسال کسر ریال دارد.", RialMoney.NotWholeErrorCode);
    }
}
