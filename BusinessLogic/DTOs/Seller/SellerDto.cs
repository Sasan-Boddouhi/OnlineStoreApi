namespace BusinessLogic.DTOs.Seller
{
    public class SellerDto
    {
        public int SellerId { get; set; }
        public int UserId { get; set; }
        public string StoreName { get; set; } = null!;
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
        public bool IsActive { get; set; }

        /// <summary>Phase 7: میانگین امتیاز نظرات تأییدشده.</summary>
        public double AverageRating { get; set; }

        /// <summary>Phase 7: تعداد نظرات تأییدشده.</summary>
        public int ReviewCount { get; set; }
    }
}