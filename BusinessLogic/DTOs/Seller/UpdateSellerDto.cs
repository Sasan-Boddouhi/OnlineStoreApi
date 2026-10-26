using System.ComponentModel.DataAnnotations;
using Application.Entities;

namespace BusinessLogic.DTOs.Seller
{
    public class UpdateSellerDto
    {
        [Required]
        [MaxLength(150)]
        public string StoreName { get; set; } = null!;

        [MaxLength(150)]
        public string? Slug { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        public SellerStatus Status { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Phase 3: نرخ کمیسیون اختصاصی (درصد). null یعنی «تغییر نده» و در نتیجه
        /// نرخ پیش‌فرض پلتفرم اعمال می‌شود.
        /// </summary>
        [Range(0, 100, ErrorMessage = "Commission rate must be between 0 and 100.")]
        public decimal? CommissionRate { get; set; }
    }
}