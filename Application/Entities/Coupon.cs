using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Coupon")]
    public class Coupon : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CouponId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = null!;

        [Column(TypeName = "decimal(5,2)")]
        public decimal DiscountPercentage { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxDiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MinOrderAmount { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        public bool IsActive { get; set; } = true;

        public int? UsageLimit { get; set; }

        public int? UsageLimitPerUser { get; set; }

        public int UsedCount { get; set; } = 0;

        /// <summary>
        /// Batch 45 (تصمیم N5): چه کسی هزینهٔ تخفیف را می‌دهد. پیش‌فرض پلتفرم: تخفیف از سهم فروشنده کم نمی‌شود
        /// و کمیسیون روی جمع اقلام پیش از تخفیف حساب می‌شود. کوپن فروشنده: سهم تخفیف هر گروه از سهم همان فروشنده
        /// کم می‌شود و کمیسیون روی مبلغ پس از تخفیف است.
        /// </summary>
        public CouponFundingSource FundedBy { get; set; } = CouponFundingSource.Platform;

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];
    }

    /// <summary>Batch 45: تأمین‌کنندهٔ هزینهٔ کوپن. در دیتابیس عدد ذخیره می‌شود (Platform = 0).</summary>
    public enum CouponFundingSource
    {
        /// <summary>پلتفرم هزینهٔ تخفیف را می‌دهد (پیش‌فرض همهٔ کوپن‌های موجود).</summary>
        Platform = 0,

        /// <summary>فروشندهٔ هر گروه سهم تخفیف گروه خودش را می‌دهد.</summary>
        Seller = 1
    }
}
