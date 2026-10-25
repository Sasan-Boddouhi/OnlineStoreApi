using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("OrderItem")]
    public class OrderItem : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int OrderItemId { get; set; }

        public int OrderId { get; set; }
        public virtual Order Order { get; set; } = null!;

        public int ProductId { get; set; }
        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        /// <summary>
        /// Phase 2 (D5): هر آیتم سفارش می‌داند کدام پیشنهاد فروش را خریده است.
        /// در فاز ۲ nullable است (dual-write tolerance) و در فاز ۳ الزامی می‌شود.
        /// </summary>
        public int ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public virtual SellerProduct Listing { get; set; } = null!;

        /// <summary>
        /// Phase 6: snapshot واریانتِ خریداری‌شده.
        /// مرجع تراکنشی همچنان ListingId است؛ این ستون فقط برای تاریخ‌نگاری دقیق است
        /// تا اگر بعداً Listing به واریانت دیگری منتقل شد، سابقهٔ سفارش تحریف نشود.
        /// </summary>
        public int? VariantId { get; set; }

        [ForeignKey(nameof(VariantId))]
        public virtual ProductVariant? Variant { get; set; }

        /// <summary>
        /// Phase 3: آیتم به گروه فروشندهٔ خود تعلق دارد.
        /// در فاز ۳ از nullable به required ارتقا می‌یابد (گام ۳ بخش ۵.۴).
        /// </summary>
        public int? OrderSellerGroupId { get; set; }

        [ForeignKey(nameof(OrderSellerGroupId))]
        public virtual OrderSellerGroup? OrderSellerGroup { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPrice { get; private set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;


        // سازنده بدون پارامتر برای EF Core (private یا protected)
        private OrderItem() { }

        // سازنده برای ایجاد OrderItem با محاسبه TotalPrice
        public OrderItem(int productId, decimal quantity, decimal unitPrice, string? description = null)
        {
            ProductId = productId;
            Quantity = quantity;
            UnitPrice = unitPrice;
            Description = description;
            TotalPrice = quantity * unitPrice;
        }

        // متد به‌روزرسانی مقدار (در صورت نیاز)
        public void UpdateQuantity(decimal newQuantity)
        {
            if (newQuantity <= 0) throw new ArgumentException("Quantity must be positive.");
            Quantity = newQuantity;
            TotalPrice = Quantity * UnitPrice;
        }
    }
}