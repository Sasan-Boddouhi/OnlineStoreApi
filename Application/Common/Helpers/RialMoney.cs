namespace Application.Common.Helpers;

/// <summary>
/// Batch 45 (FIN-06، تصمیم N6): تنها قاعدهٔ گرد کردن پول در کل سیستم.
/// <para>
/// همهٔ مبالغ ریال کامل‌اند. تخفیف و کمیسیون با «نیمه به دور از صفر» به ریال کامل گرد می‌شوند
/// (<see cref="MidpointRounding.AwayFromZero"/>)؛ باقیماندهٔ هر تقسیم به بزرگ‌ترین گروه می‌رسد (در <c>PricingService</c>، Batch 45b).
/// جمع هر قلم (مقدار × قیمت واحد) هم با همین قاعده گرد می‌شود (<see cref="LineTotal"/>).
/// هم سرویس قیمت‌گذاری و هم موجودیت گروه فروشنده همین توابع را صدا می‌زنند تا دو قاعدهٔ متفاوت هرگز کنار هم نباشند.
/// </para>
/// </summary>
public static class RialMoney
{
    /// <summary>کد خطای 409 وقتی مبلغی که باید ریال کامل باشد، کسر ریال دارد.</summary>
    public const string NotWholeErrorCode = "AMOUNT_NOT_WHOLE_RIAL";

    /// <summary>پیام کاربر برای ورودی پولی که ریال کامل نیست؛ اعتبارسنج‌ها هم همین متن را برمی‌گردانند.</summary>
    public const string NotWholeMessage = "مبلغ باید عدد صحیح ریال باشد (بدون اعشار).";

    /// <summary>گرد کردن به ریال کامل، نیمه به دور از صفر (۰٫۵ ⇒ ۱، −۰٫۵ ⇒ −۱).</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Batch 45b: جمع یک قلم (سبد یا سفارش) = مقدار × قیمت واحد، گردشده به ریال کامل (نیمه به دور از صفر).
    /// مقدار کسری مجاز است (کالای کیلویی/لیتری/متری)؛ بدون این گرد کردن ۰٫۵ × ۱۲٬۳۴۵ = ۶٬۱۷۲٫۵ جمع گروه را کسری می‌کرد
    /// و checkout با 409 AMOUNT_NOT_WHOLE_RIAL شکست می‌خورد. سبد و سفارش هر دو همین تابع را صدا می‌زنند.
    /// </summary>
    public static decimal LineTotal(decimal quantity, decimal unitPrice) => Round(quantity * unitPrice);

    /// <summary>آیا مبلغ کسر ریال ندارد؟</summary>
    public static bool IsWhole(decimal amount) => amount == decimal.Truncate(amount);

    /// <summary>
    /// کمیسیون یک گروه: <paramref name="commissionBase"/> × نرخ ÷ ۱۰۰، گردشده به ریال کامل.
    /// برای پایهٔ صحیح و نرخ ۰ تا ۱۰۰ نتیجه هرگز از پایه بیشتر نمی‌شود.
    /// </summary>
    public static decimal Commission(decimal commissionBase, decimal ratePercent)
        => Round(commissionBase * ratePercent / 100m);

    /// <summary>
    /// سهم فروشنده (تصمیم‌های N4/N5 و اصلاحیهٔ مالک پس از Batch 44): جمع اقلام گروه منهای تخفیفی که خود فروشنده
    /// تأمین می‌کند منهای کمیسیون، به‌علاوهٔ هزینهٔ ارسال بستهٔ خودش (فروشنده ارسال می‌کند و این مبلغ به او بستانکار می‌شود).
    /// تخفیف کوپن پلتفرم کسر نمی‌شود. ناوردا: Σ سهم فروشنده + Σ کمیسیون − هزینهٔ کوپن پلتفرم = پرداختی مشتری.
    /// </summary>
    public static decimal SellerPayable(decimal subTotal, decimal sellerFundedDiscount, decimal commission, decimal shippingAmount)
        => subTotal - sellerFundedDiscount - commission + shippingAmount;
}
