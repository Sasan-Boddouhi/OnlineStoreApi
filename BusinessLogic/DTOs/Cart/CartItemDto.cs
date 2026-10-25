namespace BusinessLogic.DTOs.Cart
{
    public class CartItemDto
    {
        public int CartItemId { get; set; }
        public int ListingId { get; set; }
        public int SellerId { get; set; }
        public string SellerStoreName { get; set; } = null!;
        public string ProductName { get; set; } = null!;

        /// <summary>
        /// Phase 7: نام واریانت انتخاب‌شده. بدون آن، دو تنوع از یک محصول
        /// در سبد دقیقاً یکسان به‌نظر می‌رسند.
        /// </summary>
        public string? VariantName { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal LineTotal { get; set; }
    }
}