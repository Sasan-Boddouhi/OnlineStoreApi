namespace BusinessLogic.DTOs.Cart
{
    /// <summary>
    /// اطلاعات لازم برای تبدیل سبد به سفارش.
    /// آدرس حمل فقط اینجا اجباری می‌شود، نه هنگام افزودن به سبد.
    /// </summary>
    public class CheckoutDto
    {
        public string ShippingFullName { get; set; } = null!;
        public string ShippingAddress { get; set; } = null!;
        public string ShippingPhoneNumber { get; set; } = null!;
        public string? Description { get; set; }
    }
}