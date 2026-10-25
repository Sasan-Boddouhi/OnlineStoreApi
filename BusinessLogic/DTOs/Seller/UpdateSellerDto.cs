using Application.Entities;

namespace BusinessLogic.DTOs.Seller
{
    public class UpdateSellerDto
    {
        public string StoreName { get; set; } = null!;
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public SellerStatus Status { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Phase 3: نرخ کمیسیون اختصاصی (درصد). null یعنی «تغییر نده» و در نتیجه
        /// نرخ پیش‌فرض پلتفرم اعمال می‌شود.
        /// </summary>
        public decimal? CommissionRate { get; set; }
    }
}