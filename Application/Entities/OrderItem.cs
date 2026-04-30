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