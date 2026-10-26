using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 6 — تنوع محصول (مثلاً «۲۵۶ گیگابایت مشکی»).
    ///
    /// سلسله‌مراتب قفل‌شده: Product → ProductVariant → SellerProduct (Listing) → Inventory.
    /// فروشنده «واریانت مشخص» را می‌فروشد، نه محصول کلی را — همان الگوی دیجی‌کالا.
    /// </summary>
    [Table("ProductVariant")]
    public class ProductVariant : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int VariantId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        /// <summary>نام نمایشی واریانت، مثلاً «۲۵۶GB / مشکی».</summary>
        [MaxLength(200)]
        public string? Name { get; set; }

        [MaxLength(64)]
        public string? Sku { get; set; }

        [MaxLength(20)]
        public string? Barcode { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>پیشنهادهای فروش این واریانت.</summary>
        public virtual ICollection<SellerProduct> Listings { get; set; } = new HashSet<SellerProduct>();

        /// <summary>آیتم‌های سفارشی که این واریانت را خریداری کرده‌اند.</summary>
        public virtual ICollection<OrderItem> OrderItems { get; set; } = new HashSet<OrderItem>();

        /// <summary>مقادیر ویژگی‌ها (رنگ، سایز، ...).</summary>
        public virtual ICollection<ProductAttributeValue> AttributeValues { get; set; } = new HashSet<ProductAttributeValue>();
    }
}