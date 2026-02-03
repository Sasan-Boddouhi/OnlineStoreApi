using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Invoice")]
    public class Invoice : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int InvoiceId { get; set; }

        [Required]
        public required int OrderId { get; set; }
        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public required string InvoiceNumber { get; set; }

        [Required]
        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TaxAmount { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Required]
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

        public DateTime? ConfirmedDate { get; set; }
        public DateTime? ShippedDate { get; set; }
        public DateTime? DeliveredDate { get; set; }
        public DateTime? CancelledDate { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        // Navigation property
        public virtual ICollection<Payment> Payments { get; set; } = new HashSet<Payment>();
    }

    public enum InvoiceStatus
    {
        Unpaid = 0,
        Paid = 1,
        Overdue = 2,
        Cancelled = 3
    }
}