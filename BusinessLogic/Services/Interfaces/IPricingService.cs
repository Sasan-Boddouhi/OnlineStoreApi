using BusinessLogic.DTOs.Pricing;

namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// Batch 45 (FIN-04/06/09/10/14): تنها محل محاسبهٔ پول یک سفارش — جمع اقلام، تخفیف کوپن، هزینهٔ ارسال هر بسته،
/// مبلغ قابل پرداخت، و برای هر گروه فروشنده: کمیسیون، هزینهٔ ارسال متعلق به ارسال‌کننده و سهم فروشنده.
/// خالص است: دیتابیس، ساعت و تنظیمات نمی‌خواند؛ هر چه لازم دارد در <see cref="PricingRequest"/> می‌آید.
/// </summary>
public interface IPricingService
{
    /// <summary>
    /// قیمت‌گذاری کامل سفارش با ریال کامل و یک قاعدهٔ گرد کردن (<c>RialMoney</c>).
    /// ورودی پولی که ریال کامل نباشد با کد <c>AMOUNT_NOT_WHOLE_RIAL</c> رد می‌شود.
    /// </summary>
    PricingResult Calculate(PricingRequest request);
}
