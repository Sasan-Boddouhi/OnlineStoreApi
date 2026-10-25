using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 5 — گالری تصاویر محصول.
    ///
    /// ⚠️ Product.ImageUrl (فاز ۰) دست‌نخورده می‌ماند و نقش «تصویر شاخص سریع» را دارد؛
    /// این جدول گالری کامل را نگه می‌دارد. IsPrimary باید در هر محصول حداکثر یکی باشد
    /// (ایندکس یکتای فیلترشده در AppDbContext).
    /// </summary>
    [Table("ProductImage")]
    public class ProductImage : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProductImageId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        [Required]
        [MaxLength(500)]
        public string Url { get; set; } = null!;

        [MaxLength(200)]
        public string? Alt { get; set; }

        public int SortOrder { get; set; }

        public bool IsPrimary { get; set; }
    }
}