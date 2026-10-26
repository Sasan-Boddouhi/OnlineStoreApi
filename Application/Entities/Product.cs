using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Product")]
    public class Product : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProductId { get; set; }
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }
        [MaxLength(250)]
        public string? Description { get; set; }
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Required]
        public UnitOfMeasurement Unit { get; set; }
        [MaxLength(20)]
        public string? Barcode { get; set; }
        [MaxLength(500)]
        public string? ImageUrl { get; set; }
        public DateTime? ExpirationDate { get; set; }

        [Required]
        public required int SubcategoryId { get; set; }

        [ForeignKey("SubcategoryId")]
        public virtual ProductSubcategory Subcategory { get; set; } = null!;

        [Required]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Phase 6: موجودی و پیشنهاد فروش دیگر مستقیم به Product وصل نیستند.
        /// مسیر: Product → ProductVariant → SellerProduct (Listing) → Inventory.
        /// </summary>
        public virtual ICollection<ProductVariant> Variants { get; set; } = new HashSet<ProductVariant>();

        /// <summary>Phase 6: تعریف ویژگی‌های محصول (رنگ، سایز، ...).</summary>
        public virtual ICollection<ProductAttribute> Attributes { get; set; } = new HashSet<ProductAttribute>();

        /// <summary>Phase 7: نظرات تأییدشده مبنای محاسبهٔ میانگین امتیاز هستند.</summary>
        public virtual ICollection<ProductReview> Reviews { get; set; } = new HashSet<ProductReview>();

        // ---------------- Phase 9 (R33): کش امتیاز ----------------
        // در فاز ۷ عمداً cacheنشدیم؛ اما با رشد کاتالوگ، زیرکوئری AVG/COUNT روی هر
        // خواندن محصول گران شد. اکنون cache داریم و **هر مسیری که تأیید نظر را
        // تغییر دهد باید Recalculate را صدا بزند** (ReviewRatingCacheService).
        /// <summary>میانگین امتیاز نظرات تأییدشده (کش).</summary>
        public double AverageRating { get; set; }

        /// <summary>تعداد نظرات تأییدشده (کش).</summary>
        public int ReviewCount { get; set; }

        /// <summary>Phase 5: گالری تصاویر محصول.</summary>
        public virtual ICollection<ProductImage> Images { get; set; } = new HashSet<ProductImage>();
    }
    public enum UnitOfMeasurement
    {
        Piece,          // عدد
        Kilogram,       // کیلوگرم
        gram,           // گرم  
        Liter,          // لیتر
        Meter,          // متر
        Centimeter,     // سانتیمتر
        Millimeter      // میلیمتر
    }
}
