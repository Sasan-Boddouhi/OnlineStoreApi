using Application.Exceptions;
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

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; private set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingCostShare { get; private set; } = 0;

        /// <summary>SubTotal − DiscountAmount − ShippingCostShare − CommissionAmount؛ همان چیزی که به فروشنده می‌رسد.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal SellerPayable { get; private set; }

        /// <summary>
        /// Batch 4 (F-02): وضعیت فقط از متدهای گذار همین موجودیت تغییر می‌کند؛
        /// setter عمومی ماشین حالت را دور می‌زد.
        /// </summary>
        public SellerGroupStatus Status { get; private set; } = SellerGroupStatus.Pending;

        [MaxLength(100)]
        public string? TrackingNumber { get; set; }

        [MaxLength(100)]
        public string? CarrierName { get; set; }

        public DateTime? ShippedAtUtc { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];

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

        public void SetDiscountAndShippingShare(decimal discountAmount, decimal shippingCostShare)
        {
            DiscountAmount = Math.Max(0, discountAmount);
            ShippingCostShare = Math.Max(0, shippingCostShare);
            SellerPayable = Math.Max(0, SubTotal - DiscountAmount - ShippingCostShare - CommissionAmount);
        }

        // ---------------- Batch 4 (F-02): ماشین حالت گروه ----------------
        //
        //   Pending    --ApplyCommission--> Processing   (پرداخت/تأیید سفارش)
        //   Processing --MarkShipped------> Shipped
        //   Shipped    --MarkDelivered----> Delivered
        //   Delivered  --MarkReturned-----> Returned     (تنها خروج از Delivered)
        //   Pending | Processing | Shipped --Cancel--> Cancelled
        //   Cancelled پایانی است؛ Cancel دوباره روی آن بی‌اثر است.
        //   Returned پایانی است.
        //
        // هر گذار دیگری InvalidOperationException می‌دهد و وضعیت را دست نمی‌زند.
        // لایهٔ سرویس پیش از صدا زدن این متدها همین قواعد را با خطای 409 گزارش می‌کند.

        /// <summary>
        /// کمیسیون را قفل می‌کند و گروه را از Pending به Processing می‌برد.
        /// فقط یک بار در چرخهٔ سفارش (هنگام پرداخت/تأیید) صدا زده می‌شود.
        /// </summary>
        public void ApplyCommission()
        {
            if (Status != SellerGroupStatus.Pending)
                throw new BusinessRuleViolationException($"Commission can only be applied to a pending group (current: {Status}).", "GROUP_INVALID_STATE");

            if (CommissionRate < 0 || CommissionRate > 100)
                throw new BusinessRuleViolationException("Commission rate is out of range.", "COMMISSION_RATE_INVALID");

            CommissionAmount = Math.Round(SubTotal * CommissionRate / 100m, 2, MidpointRounding.AwayFromZero);
            SellerPayable = Math.Max(0, SubTotal - DiscountAmount - ShippingCostShare - CommissionAmount);
            Status = SellerGroupStatus.Processing;
        }

        /// <summary>
        /// ارسال فقط از Processing؛ یعنی فقط پس از پرداخت و تأیید سفارش.
        /// گروه Pending (سفارش پرداخت‌نشده) قابل ارسال نیست.
        /// </summary>
        public void MarkShipped(string? carrierName, string? trackingNumber, DateTime shippedAtUtc)
        {
            if (Status != SellerGroupStatus.Processing)
                throw new BusinessRuleViolationException($"Only processing groups can be shipped (current: {Status}).", "GROUP_INVALID_STATE");

            Status = SellerGroupStatus.Shipped;
            CarrierName = carrierName;
            TrackingNumber = trackingNumber;
            ShippedAtUtc = shippedAtUtc;
        }

        /// <summary>تحویل فقط از Shipped؛ گروهی که ارسال نشده، تحویل هم نمی‌شود.</summary>
        public void MarkDelivered(DateTime deliveredAt)
        {
            if (Status != SellerGroupStatus.Shipped)
                throw new BusinessRuleViolationException($"Only shipped groups can be delivered (current: {Status}).", "GROUP_INVALID_STATE");

            Status = SellerGroupStatus.Delivered;
            DeliveredAt = deliveredAt;
        }

        /// <summary>
        /// لغو از Pending/Processing/Shipped. روی گروه لغوشده بی‌اثر است (idempotent).
        /// گروه تحویل‌شده یا مرجوعی لغو نمی‌شود؛ مسیر آن برگشت/مرجوعی است.
        /// </summary>
        public void Cancel()
        {
            if (Status == SellerGroupStatus.Cancelled)
                return;

            if (Status is SellerGroupStatus.Delivered or SellerGroupStatus.Returned)
                throw new BusinessRuleViolationException($"A {Status} group cannot be cancelled.", "GROUP_INVALID_STATE");

            Status = SellerGroupStatus.Cancelled;
        }

        public void MarkReturned()
        {
            if (Status != SellerGroupStatus.Delivered)
                throw new BusinessRuleViolationException("Only delivered groups can be returned.", "GROUP_INVALID_STATE");

            Status = SellerGroupStatus.Returned;
        }
    }

    public enum SellerGroupStatus
    {
        Pending = 0,
        Processing = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4,
        Returned = 5
    }
}