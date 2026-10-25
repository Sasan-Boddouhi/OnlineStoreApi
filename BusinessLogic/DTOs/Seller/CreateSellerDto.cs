namespace BusinessLogic.DTOs.Seller
{
    public class CreateSellerDto
    {
        public int UserId { get; set; }
        public string StoreName { get; set; } = null!;
        public string? Slug { get; set; }
        public string? Description { get; set; }
    }
}