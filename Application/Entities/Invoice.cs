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
        public int OrderId { get; set; }

        [ForeignKey(nameof(OrderId))]
        public virtual Order Order { get; set; } = null!;

        [Required, MaxLength(50)]
        public string InvoiceNumber { get; set; } = null!;

        public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TaxAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        public InvoiceStatus Status { get; private set; } = InvoiceStatus.Unpaid;

        public DateTime? PaidDate { get; private set; }

        public virtual ICollection<Payment> Payments { get; set; } = new HashSet<Payment>();

        public void MarkAsPaid()
        {
            Status = InvoiceStatus.Paid;
            PaidDate = DateTime.UtcNow;
        }
    }

    public enum InvoiceStatus
    {
        Unpaid = 0,
        Paid = 1,
        Overdue = 2,
        Cancelled = 3
    }
}