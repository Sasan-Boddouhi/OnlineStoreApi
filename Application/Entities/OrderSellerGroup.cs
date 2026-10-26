using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Application.Entities
{
    /// <summary>
    /// Phase 3 — زیرسفارش هر فروشنده در یک سفارش.
    /// یک سفارش می‌تواند چند گروه داشته باشد (هر فروشنده یک گروه)، و هر گروه
    /// چرخهٔ fulfillment و تسویهٔ مستقل خودش را دارد.
    /// </summary>
    [Table("OrderSellerGroup")]
    [Index(nameof(OrderId), nameof(SellerId), IsUnique = true)]
    public class OrderSellerGroup : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int OrderSellerGroupId { get; private set; }

        public int OrderId { get; private set; }

        [ForeignKey(nameof(OrderId))]
        public virtual Order Order { get; private set; } = null!;

        public int SellerId { get; private set; }

        [ForeignKey(nameof(SellerId))]
        public virtual Seller Seller { get; private set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; private set; }

        /// <summary>
        /// نرخ کمیسیون به درصد (مثلاً 10.00). ⚠️ snapshot تاریخی است:
        /// تغییر نرخ فروشنده در آینده نباید تسویهٔ سفارش‌های گذشته را بازنویسی کند.
        /// </summary>
        [Column(TypeName = "decimal(5,2)")]
        public decimal CommissionRate { get; private set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CommissionAmount { get; private set; }

        /// <summary>SubTotal − CommissionAmount؛ همان چیزی که به فروشنده می‌رسد.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal SellerPayable { get; private set; }

        public SellerGroupStatus Status { get; private set; } = SellerGroupStatus.Pending;

        /// <summary>
        /// Phase 8 (R35): لحظهٔ واقعی تحویل.
        /// گزارش‌های مالی باید بر این مبنا فیلتر شوند، نه بر تاریخ ثبت سفارش؛
        /// وگرنه سفارشی که ماه پیش ثبت و این ماه تحویل شده، در ماه اشتباه حساب می‌شود.
        /// </summary>
        public DateTime? DeliveredAt { get; private set; }

        public virtual ICollection<OrderItem> Items { get; set; } = new HashSet<OrderItem>();

        public virtual SellerPayout? Payout { get; set; }

        private OrderSellerGroup() { } // For EF

        public OrderSellerGroup(int sellerId, decimal commissionRate)
        {
            if (commissionRate < 0 || commissionRate > 100)
                throw new ArgumentException("Commission rate must be between 0 and 100.", nameof(commissionRate));

            SellerId = sellerId;
            CommissionRate = commissionRate;
        }

        /// <summary>
        /// جمع گروه از آیتم‌های همان سفارش محاسبه می‌شود (نه از یک مجموعهٔ ناقص لود نشده).
        /// </summary>
        public void RecalculateSubTotal(IEnumerable<OrderItem> orderItems)
        {
            SubTotal = orderItems
                .Where(i => ReferenceEquals(i.OrderSellerGroup, this))
                .Sum(i => i.TotalPrice);
        }

        /// <summary>
        /// کمیسیون را قفل می‌کند. فقط یک بار در چرخهٔ سفارش (هنگام تأیید) صدا زده می‌شود.
        /// </summary>
        public void ApplyCommission()
        {
            if (CommissionRate < 0 || CommissionRate > 100)
                throw new InvalidOperationException("Commission rate is out of range.");

            CommissionAmount = Math.Round(SubTotal * CommissionRate / 100m, 2, MidpointRounding.AwayFromZero);
            SellerPayable = SubTotal - CommissionAmount;
            Status = SellerGroupStatus.Processing;
        }

        public void MarkDelivered(DateTime deliveredAt)
        {
            if (Status == SellerGroupStatus.Cancelled)
                throw new InvalidOperationException("A cancelled group cannot be delivered.");

            Status = SellerGroupStatus.Delivered;
            DeliveredAt = deliveredAt;
        }

        public void Cancel()
        {
            if (Status == SellerGroupStatus.Delivered)
                throw new InvalidOperationException("A delivered group cannot be cancelled.");

            Status = SellerGroupStatus.Cancelled;
        }
    }

    public enum SellerGroupStatus
    {
        Pending = 0,
        Processing = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4
    }
}