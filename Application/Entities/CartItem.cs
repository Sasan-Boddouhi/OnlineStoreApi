using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 4 — یک قلم سبد خرید.
    ///
    /// ⚠️ UnitPrice در لحظهٔ افزودن قفل می‌شود (snapshot). اگر فروشنده بعداً قیمت را
    /// عوض کند، سبد کاربر تا زمان checkout قیمت قبلی را نشان می‌دهد و غافلگیر نمی‌شود.
    /// قیمت نهایی سفارش هم هنگام checkout از همین snapshot خوانده می‌شود.
    /// </summary>
    [Table("CartItem")]
    public class CartItem : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CartItemId { get; set; }

        [Required]
        public int CartId { get; set; }

        [ForeignKey(nameof(CartId))]
        public virtual Cart Cart { get; set; } = null!;

        [Required]
        public int ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public virtual SellerProduct Listing { get; set; } = null!;

        /// <summary>Phase 6: واریانتِ انتخاب‌شده (از روی Listing استخراج می‌شود).</summary>
        public int? VariantId { get; set; }

        [ForeignKey(nameof(VariantId))]
        public virtual ProductVariant? Variant { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        /// <summary>قیمت واحد در لحظهٔ افزودن به سبد.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        public DateTime AddedAt { get; set; }

        [NotMapped]
        public decimal LineTotal => UnitPrice * Quantity;
    }
}