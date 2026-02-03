using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Payment")]
    public class Payment : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PaymentId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(50)]
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;

        [MaxLength(100)]
        public string? TransactionId { get; set; }

        [Required]
        public PaymentStatus Status { get; set; } = PaymentStatus.Completed;

        [MaxLength(500)]
        public string? Notes { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public required int InvoiceId { get; set; }
        [ForeignKey("InvoiceId")]
        public virtual Invoice Invoice { get; set; } = null!;
    }

    public enum PaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2,
        Refunded = 3
    }

    public enum PaymentMethod
    {
        CreditCard = 0,
        Cash = 1,
        BankTransfer = 2,
        BankCheck = 3,
        PersonalCheck = 4
    }
}