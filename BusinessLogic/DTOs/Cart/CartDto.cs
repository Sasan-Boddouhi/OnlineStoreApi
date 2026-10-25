namespace BusinessLogic.DTOs.Cart
{
    public class CartDto
    {
        public int CartId { get; set; }
        public bool IsAnonymous { get; set; }
        public List<CartItemDto> Items { get; set; } = new();
        public decimal TotalAmount { get; set; }
        public int TotalItems { get; set; }
    }
}