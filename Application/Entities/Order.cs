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

        [Required]
        public DateTime OrderDate { get; set; } = DateTime.Now;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        [MaxLength(500)]
        public string? Notes { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        public bool IsConfirmed { get; set; } = false;

        // Navigation properties
        public virtual ICollection<OrderItem> OrderItems { get; set; } = new HashSet<OrderItem>();

        [Required]
        public required int CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; } = null!;
        public virtual Invoice? Invoice { get; set; }
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