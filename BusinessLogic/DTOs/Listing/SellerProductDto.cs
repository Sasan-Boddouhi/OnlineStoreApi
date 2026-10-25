namespace BusinessLogic.DTOs.Listing
{
    public class SellerProductDto
    {
        public int ListingId { get; set; }
        public int SellerId { get; set; }
        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public decimal Price { get; set; }
        public string Status { get; set; } = null!;
    }
}