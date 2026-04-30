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

        // Shipping Snapshot
        [Required, MaxLength(200)]
        public string ShippingFullName { get; set; } = null!;

        [Required, MaxLength(500)]
        public string ShippingAddress { get; set; } = null!;

        [Required, MaxLength(20)]
        public string ShippingPhoneNumber { get; set; } = null!;

        public int CustomerId { get; set; }
        public virtual Customer Customer { get; set; } = null!;

        public virtual ICollection<OrderItem> OrderItems { get; set; } = new HashSet<OrderItem>();
        public virtual Invoice? Invoice { get; set; }

        public void AddItem(OrderItem item)
        {
            item.OrderId = OrderId;
            OrderItems.Add(item);
            RecalculateTotal();
        }

        public void RemoveItem(OrderItem item)
        {
            OrderItems.Remove(item);
            RecalculateTotal();
        }

        public void Confirm()
        {
            if (Status != OrderStatus.Pending)
                throw new InvalidOperationException("Only pending orders can be confirmed.");
            Status = OrderStatus.Processing;
        }

        public void Cancel()
        {
            if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
                throw new InvalidOperationException("Cannot cancel shipped or delivered orders.");
            Status = OrderStatus.Cancelled;
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