using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 3 — تسویهٔ مالی فروشنده برای یک گروه سفارش تحویل‌شده.
    /// رابطهٔ ۱:۱ با <see cref="OrderSellerGroup"/> تضمین می‌کند تحویل تکراری،
    /// پرداخت تکراری نسازد.
    /// </summary>
    [Table("SellerPayout")]
    public class SellerPayout : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SellerPayoutId { get; private set; }

        public int SellerId { get; private set; }

        [ForeignKey(nameof(SellerId))]
        public virtual Seller Seller { get; private set; } = null!;

        /// <summary>گروه سفارشی که این پرداخت را ایجاد کرده است (قابل null برای پرداخت‌های دوره‌ای دستی).</summary>
        public int? OrderSellerGroupId { get; private set; }

        [ForeignKey(nameof(OrderSellerGroupId))]
        public virtual OrderSellerGroup? OrderSellerGroup { get; private set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; private set; }

        public PayoutStatus Status { get; private set; } = PayoutStatus.Pending;

        public DateTime PeriodStart { get; private set; }

        public DateTime PeriodEnd { get; private set; }

        [MaxLength(100)]
        public string? Reference { get; set; }

        public DateTime? PaidAt { get; private set; }

        private SellerPayout() { } // For EF

        public SellerPayout(
            int sellerId,
            int? orderSellerGroupId,
            decimal amount,
            DateTime periodStart,
            DateTime periodEnd)
        {
            if (amount < 0)
                throw new ArgumentException("Payout amount cannot be negative.", nameof(amount));

            if (periodEnd < periodStart)
                throw new ArgumentException("Payout period end cannot precede its start.", nameof(periodEnd));

            SellerId = sellerId;
            OrderSellerGroupId = orderSellerGroupId;
            Amount = amount;
            PeriodStart = periodStart;
            PeriodEnd = periodEnd;
        }

        public void MarkPaid(string? reference)
        {
            if (Status != PayoutStatus.Pending)
                throw new InvalidOperationException("Only pending payouts can be marked as paid.");

            Status = PayoutStatus.Paid;
            PaidAt = DateTime.UtcNow;
            Reference = reference;
        }

        public void Cancel()
        {
            if (Status == PayoutStatus.Paid)
                throw new InvalidOperationException("A paid payout cannot be cancelled.");

            Status = PayoutStatus.Cancelled;
        }
    }

    public enum PayoutStatus
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2
    }
}