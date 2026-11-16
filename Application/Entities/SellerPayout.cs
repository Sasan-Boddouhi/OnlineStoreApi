using Application.Exceptions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 3 — تسویهٔ مالی فروشنده برای یک گروه سفارش تحویل‌شده.
    /// رابطهٔ ۱:۱ با <see cref="OrderSellerGroup"/> تضمین می‌کند تحویل تکراری،
    /// پرداخت تکراری نسازد.
    /// <para>
    /// Batch 44 (FIN-02، F-09): <see cref="Amount"/> مبلغ ناخالص (SellerPayable گروه) است و هرگز تغییر نمی‌کند.
    /// هنگام پرداخت، بدهی‌های باز فروشنده از آن کم می‌شود: <see cref="DebtOffsetAmount"/> بخش کم‌شده و
    /// <see cref="NetAmount"/> = Amount − DebtOffsetAmount همان مبلغی است که ادمین واقعاً به حساب فروشنده واریز می‌کند.
    /// تا پیش از پرداخت این دو ستون «۰ و Amount» هستند (چیزی کسر نشده)؛ پیش‌نمایش کسر بدهی در فهرست‌ها محاسبه می‌شود.
    /// </para>
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

        /// <summary>مبلغ ناخالص تسویه (سهم فروشنده از گروه). پس از ساخت تغییر نمی‌کند.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; private set; }

        /// <summary>Batch 44 (F-09): بخشی از <see cref="Amount"/> که هنگام پرداخت بابت بدهی‌های فروشنده کسر شد.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal DebtOffsetAmount { get; private set; }

        /// <summary>Batch 44 (F-09): مبلغ واریزی واقعی = <see cref="Amount"/> − <see cref="DebtOffsetAmount"/>.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal NetAmount { get; private set; }

        public PayoutStatus Status { get; private set; } = PayoutStatus.Pending;

        public DateTime PeriodStart { get; private set; }

        public DateTime PeriodEnd { get; private set; }

        /// <summary>
        /// Batch 44 (FIN-02): از این لحظه (UTC) تسویه قابل پرداخت است؛ برای تسویهٔ بسته = زمان تحویل + روزهای نگه‌داشت
        /// (<c>PayoutSettings:HoldDays</c>، پیش‌فرض ۷). پرداخت پیش از آن رد می‌شود (PAYOUT_ON_HOLD).
        /// </summary>
        public DateTime AvailableAtUtc { get; private set; }

        [MaxLength(100)]
        public string? Reference { get; set; }

        public DateTime? PaidAt { get; private set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];

        private SellerPayout() { } // For EF

        /// <param name="sellerId">فروشنده.</param>
        /// <param name="orderSellerGroupId">گروه سفارش (برای تسویهٔ دوره‌ای دستی null).</param>
        /// <param name="amount">مبلغ ناخالص؛ منفی مجاز نیست.</param>
        /// <param name="periodStart">آغاز دوره.</param>
        /// <param name="periodEnd">پایان دوره.</param>
        /// <param name="availableAtUtc">
        /// Batch 44: لحظهٔ قابل پرداخت شدن. اگر داده نشود همان <paramref name="periodEnd"/> است
        /// (تسویهٔ دوره‌ای دستی که دوره‌اش بسته شده نگه‌داشت ندارد).
        /// </param>
        public SellerPayout(
            int sellerId,
            int? orderSellerGroupId,
            decimal amount,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime? availableAtUtc = null)
        {
            if (amount < 0)
                throw new ArgumentException("Payout amount cannot be negative.", nameof(amount));

            if (periodEnd < periodStart)
                throw new ArgumentException("Payout period end cannot precede its start.", nameof(periodEnd));

            var availableAt = availableAtUtc ?? periodEnd;
            if (availableAt < periodStart)
                throw new ArgumentException("Payout availability cannot precede its period start.", nameof(availableAtUtc));

            SellerId = sellerId;
            OrderSellerGroupId = orderSellerGroupId;
            Amount = amount;
            DebtOffsetAmount = 0m;
            NetAmount = amount;
            PeriodStart = periodStart;
            PeriodEnd = periodEnd;
            AvailableAtUtc = availableAt;
        }

        /// <summary>آیا در لحظهٔ <paramref name="nowUtc"/> هنوز در دورهٔ نگه‌داشت است.</summary>
        public bool IsOnHold(DateTime nowUtc) => nowUtc < AvailableAtUtc;

        /// <summary>
        /// Batch 44: ثبت پرداخت. <paramref name="debtOffsetAmount"/> بخشی از مبلغ است که بابت بدهی فروشنده کسر شد
        /// (۰ تا <see cref="Amount"/>)؛ <see cref="NetAmount"/> مبلغ واریزی است.
        /// </summary>
        public void MarkPaid(string? reference, decimal debtOffsetAmount, DateTime paidAtUtc)
        {
            if (Status != PayoutStatus.Pending)
                throw new BusinessRuleViolationException("Only pending payouts can be marked as paid.", "PAYOUT_INVALID_STATE");

            if (IsOnHold(paidAtUtc))
                throw new BusinessRuleViolationException("The payout is still inside its hold period.", ErrorCodes.PayoutOnHold.Code);

            if (debtOffsetAmount < 0 || debtOffsetAmount > Amount)
                throw new ArgumentOutOfRangeException(nameof(debtOffsetAmount), "Debt offset must be between zero and the payout amount.");

            Status = PayoutStatus.Paid;
            PaidAt = paidAtUtc;
            Reference = reference;
            DebtOffsetAmount = debtOffsetAmount;
            NetAmount = Amount - debtOffsetAmount;
        }

        public void Cancel()
        {
            if (Status == PayoutStatus.Paid)
                throw new BusinessRuleViolationException("A paid payout cannot be cancelled.", "PAYOUT_INVALID_STATE");

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
