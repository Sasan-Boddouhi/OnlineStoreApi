using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// پروفایل فروشگاه (Phase 2). نام کلاس «Seller» است، مفهوم «ShopProfile».
    /// رابطه ۱:۱ با User (UserId یکتاست) — منطبق با الگوی Customer/Employee.
    /// ⚠️ UserType دست‌نخورده می‌ماند (D3): یک کاربر می‌تواند هم Customer باشد هم Seller.
    /// </summary>
    [Table("Seller")]
    public class Seller : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SellerId { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string StoreName { get; set; } = null!;

        /// <summary>شناسه URL فروشگاه. یکتای غیرتهی؛ برای فروشندگان بدون اسلاگ NULL می‌ماند.</summary>
        [MaxLength(150)]
        public string? Slug { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        public SellerStatus Status { get; set; } = SellerStatus.Pending;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Phase 3: نرخ کمیسیون اختصاصی فروشنده (درصد). null یعنی نرخ پیش‌فرض پلتفرم
        /// از <c>CommissionSettings:DefaultRate</c> خوانده شود.
        /// </summary>
        [Column(TypeName = "decimal(5,2)")]
        public decimal? CommissionRate { get; set; }

        public virtual ICollection<SellerProduct> Listings { get; set; } = new HashSet<SellerProduct>();

        /// <summary>Phase 7: نظرات تأییدشده مبنای میانگین امتیاز فروشنده هستند.</summary>
        public virtual ICollection<SellerReview> Reviews { get; set; } = new HashSet<SellerReview>();

        // ---------------- Phase 9 (R33): کش امتیاز ----------------
        /// <summary>میانگین امتیاز نظرات تأییدشده (کش).</summary>
        public double AverageRating { get; set; }

        /// <summary>تعداد نظرات تأییدشده (کش).</summary>
        public int ReviewCount { get; set; }

        public virtual ICollection<Warehouse> Warehouses { get; set; } = new HashSet<Warehouse>();
    }

    public enum SellerStatus
    {
        Pending = 0,
        Approved = 1,
        Suspended = 2,
        Rejected = 3
    }
}