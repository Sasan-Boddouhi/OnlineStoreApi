using BusinessLogic.DTOs.Cart;
using BusinessLogic.Validators.Common;
using FluentValidation;

namespace BusinessLogic.Validators.Cart;

public class CheckoutDtoValidator : AbstractValidator<CheckoutDto>
{
    public CheckoutDtoValidator()
    {
        RuleFor(x => x.ShippingFullName)
            .NotEmpty().WithMessage("نام تحویل‌گیرنده الزامی است.")
            .MaximumLength(100).WithMessage("نام تحویل‌گیرنده نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.ShippingAddress)
            .NotEmpty().WithMessage("آدرس تحویل الزامی است.")
            .MaximumLength(500).WithMessage("آدرس تحویل نباید بیش از ۵۰۰ کاراکتر باشد.");

        RuleFor(x => x.ShippingPhoneNumber)
            .NotEmpty().WithMessage("شماره تماس الزامی است.")
            .MustBeIranianMobile();

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("توضیحات نباید بیش از ۵۰۰ کاراکتر باشد.");

        // Batch 6 (F-11): شناسهٔ ارسال‌شده باید معتبر باشد. اجباری‌بودن آن به وجود روش ارسال فعال
        // بستگی دارد و در CartService.CheckoutAsync (SHIPPING_METHOD_REQUIRED) سنجیده می‌شود.
        RuleFor(x => x.ShippingMethodId)
            .GreaterThan(0).When(x => x.ShippingMethodId.HasValue)
            .WithMessage("شناسهٔ روش ارسال معتبر نیست.");

        // Batch 48 (F-25): طول‌ها برابر ستون‌های Order / Coupon.
        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(100).WithMessage("کلید یکتایی نباید بیش از ۱۰۰ کاراکتر باشد.");

        RuleFor(x => x.CouponCode)
            .MaximumLength(50).WithMessage("کد تخفیف نباید بیش از ۵۰ کاراکتر باشد.");
    }
}
