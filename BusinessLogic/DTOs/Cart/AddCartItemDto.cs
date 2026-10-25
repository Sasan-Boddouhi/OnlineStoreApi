namespace BusinessLogic.DTOs.Cart
{
    public class AddCartItemDto
    {
        public int ListingId { get; set; }
        public decimal Quantity { get; set; } = 1;
    }
}