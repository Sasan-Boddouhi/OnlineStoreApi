using Application.Entities;

namespace BusinessLogic.DTOs.Pricing;

/// <summary>Batch 45: یک گروه فروشنده در ورودی قیمت‌گذاری (جمع اقلام همان فروشنده و نرخ کمیسیون snapshot شده).</summary>
/// <param name="SellerId">شناسهٔ فروشنده؛ ترتیب گروه‌ها در تقسیم بر اساس همین شناسه است.</param>
/// <param name="SubTotal">جمع اقلام گروه (ریال کامل).</param>
/// <param name="CommissionRate">نرخ کمیسیون به درصد (۰ تا ۱۰۰).</param>
public sealed record PricingGroupInput(int SellerId, decimal SubTotal, decimal CommissionRate);

/// <summary>Batch 45: کوپن معتبرشده (انقضا، سقف استفاده و حداقل سفارش پیش‌تر سنجیده شده‌اند).</summary>
/// <param name="DiscountPercentage">درصد تخفیف روی جمع اقلام (۰ تا ۱۰۰).</param>
/// <param name="MaxDiscountAmount">سقف مبلغ تخفیف (ریال)؛ null یعنی بی‌سقف.</param>
/// <param name="FundedBy">تأمین‌کنندهٔ هزینهٔ تخفیف (تصمیم N5).</param>
public sealed record PricingCoupon(decimal DiscountPercentage, decimal? MaxDiscountAmount, CouponFundingSource FundedBy);

/// <summary>Batch 45: ورودی کامل قیمت‌گذاری یک سفارش. سرویس به دیتابیس دست نمی‌زند.</summary>
/// <param name="Groups">گروه‌های فروشنده (دست‌کم یکی).</param>
/// <param name="ShippingFeePerParcel">هزینهٔ پایهٔ روش ارسال؛ هر گروه یک بسته است (تصمیم N4). ۰ یعنی بدون ارسال.</param>
/// <param name="Coupon">کوپن اعمال‌شده یا null.</param>
/// <param name="MinimumPayableAmount">کمترین مبلغ قابل پرداخت درگاه (تصمیم N7)؛ تخفیف تا این حد سقف می‌خورد.</param>
public sealed record PricingRequest(
    IReadOnlyList<PricingGroupInput> Groups,
    decimal ShippingFeePerParcel,
    PricingCoupon? Coupon,
    decimal MinimumPayableAmount);

/// <summary>Batch 45: سهم مالی یک گروه فروشنده؛ همهٔ مبالغ ریال کامل.</summary>
/// <param name="SellerId">شناسهٔ فروشنده.</param>
/// <param name="SubTotal">جمع اقلام گروه.</param>
/// <param name="CommissionRate">نرخ کمیسیون (درصد).</param>
/// <param name="DiscountAmount">سهم گروه از کل تخفیف.</param>
/// <param name="PlatformDiscountShare">بخشی از سهم تخفیف که پلتفرم می‌دهد.</param>
/// <param name="SellerFundedDiscount">بخشی از سهم تخفیف که فروشنده می‌دهد.</param>
/// <param name="ShippingAmount">هزینهٔ ارسال بستهٔ این گروه که به ارسال‌کننده (فروشنده) بستانکار می‌شود.</param>
/// <param name="CommissionAmount">کمیسیون پلتفرم.</param>
/// <param name="SellerPayable">سهم فروشنده = SubTotal − SellerFundedDiscount − CommissionAmount + ShippingAmount.</param>
public sealed record SellerGroupPricing(
    int SellerId,
    decimal SubTotal,
    decimal CommissionRate,
    decimal DiscountAmount,
    decimal PlatformDiscountShare,
    decimal SellerFundedDiscount,
    decimal ShippingAmount,
    decimal CommissionAmount,
    decimal SellerPayable);

/// <summary>
/// Batch 45: نتیجهٔ قیمت‌گذاری. ناوردا (همیشه برقرار):
/// Σ SellerPayable + Σ CommissionAmount − PlatformDiscountCost = PayableTotal (ارسال داخل SellerPayable است).
/// </summary>
/// <param name="ItemsTotal">جمع اقلام همهٔ گروه‌ها.</param>
/// <param name="DiscountAmount">تخفیف نهایی پس از سقف کوپن و سقف کمترین مبلغ پرداخت.</param>
/// <param name="DiscountCappedByMinimumPayable">آیا تخفیف برای حفظ کمترین مبلغ قابل پرداخت کم شد؟</param>
/// <param name="ShippingTotal">جمع هزینهٔ ارسال همهٔ بسته‌ها.</param>
/// <param name="PayableTotal">مبلغی که مشتری می‌پردازد = ItemsTotal − DiscountAmount + ShippingTotal.</param>
/// <param name="PlatformDiscountCost">جمع تخفیفی که پلتفرم تأمین می‌کند.</param>
/// <param name="Groups">سهم هر گروه، به ترتیب شناسهٔ فروشنده (باقیماندهٔ تقسیم روی گروه آخر).</param>
public sealed record PricingResult(
    decimal ItemsTotal,
    decimal DiscountAmount,
    bool DiscountCappedByMinimumPayable,
    decimal ShippingTotal,
    decimal PayableTotal,
    decimal PlatformDiscountCost,
    IReadOnlyList<SellerGroupPricing> Groups);
