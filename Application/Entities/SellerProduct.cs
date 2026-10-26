using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// پیشنهاد فروش یک محصول توسط یک فروشنده (Phase 2). نام کلاس «SellerProduct»، مفهوم «Listing».
    ///
    /// ⚠️ ستون Stock ندارد (D1): موجودی همیشه در Inventory است.
    /// ⚠️ Price اینجا مرجع قیمت سفارش است (D2)؛ Product.Price فقط قیمت نمایشی/پیش‌فرض است.
    /// </summary>
    [Table("SellerProduct")]
    public class SellerProduct : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ListingId { get; set; }

        [Required]
        public int SellerId { get; set; }

        [ForeignKey(nameof(SellerId))]
        public virtual Seller Seller { get; set; } = null!;

        /// <summary>
        /// Phase 6: Listing به واریانت وصل است، نه به محصول.
        /// فروشنده «۲۵۶ گیگ مشکی» را می‌فروشد، نه «گوشی» را به‌طور کلی.
        /// </summary>
        [Required]
        public int VariantId { get; set; }

        [ForeignKey(nameof(VariantId))]
        public virtual ProductVariant Variant { get; set; } = null!;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public SellerProductStatus Status { get; set; } = SellerProductStatus.Draft;

        public virtual ICollection<Inventory> Inventories { get; set; } = new HashSet<Inventory>();
        public virtual ICollection<OrderItem> OrderItems { get; set; } = new HashSet<OrderItem>();
        public virtual ICollection<CartItem> CartItems { get; set; } = new HashSet<CartItem>();

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public enum SellerProductStatus
    {
        Draft = 0,
        Active = 1,
        Inactive = 2
    }
}