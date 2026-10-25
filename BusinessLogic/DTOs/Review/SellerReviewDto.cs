namespace BusinessLogic.DTOs.Review
{
    public class SellerReviewDto
    {
        public int SellerReviewId { get; set; }
        public int SellerId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public bool IsApproved { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}