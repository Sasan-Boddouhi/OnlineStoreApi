using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Order")]
    public class Order : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int OrderId { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; private set; }

        public OrderStatus Status { get; private set; } = OrderStatus.Pending;

        // ===== Shipping Snapshot =====
        [Required, MaxLength(200)]
        public string ShippingFullName { get; set; } = null!;

        [Required, MaxLength(500)]
        public string ShippingAddress { get; set; } = null!;

        [Required, MaxLength(20)]
        public string ShippingPhoneNumber { get; set; } = null!;

        public bool IsConfirmed { get; private set; }

        public int CustomerId { get; set; }
        public virtual Customer Customer { get; set; } = null!;

        public virtual ICollection<OrderItem> OrderItems { get; set; } = new HashSet<OrderItem>();

        public virtual Invoice? Invoice { get; set; }

        // ===== Domain Methods =====

        public void AddItem(OrderItem item)
        {
            OrderItems.Add(item);
            RecalculateTotal();
        }

        public void Confirm()
        {
            IsConfirmed = true;
            Status = OrderStatus.Processing;
        }

        private void RecalculateTotal()
        {
            TotalAmount = OrderItems.Sum(i => i.Quantity * i.UnitPrice);
        }
    }

    public enum OrderStatus
    {
        Pending = 0,
        Processing = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4,
        Returned = 5
    }
}