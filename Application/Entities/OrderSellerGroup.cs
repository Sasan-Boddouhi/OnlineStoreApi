using Application.Common.Helpers;
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

        /// <summary>
        /// سهم این گروه از کل تخفیف سفارش (پلتفرمی یا فروشنده‌ای)؛ ریال کامل.
        /// سهم تأمین‌شده توسط پلتفرم در <see cref="PlatformDiscountShare"/> است و بقیه را فروشنده می‌دهد.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; private set; } = 0;

        /// <summary>
        /// فقط تاریخچه (پیش از Batch 45): سهم هزینهٔ ارسالی که به‌اشتباه از فروشنده کسر می‌شد (FIN-04).
        /// سفارش‌های جدید همیشه ۰ می‌نویسند؛ هزینهٔ ارسال هر بسته در <see cref="ShippingAmount"/> است.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingCostShare { get; private set; } = 0;

        /// <summary>
        /// Batch 45 (تصمیم N4): هزینهٔ ارسالی که مشتری برای بستهٔ همین فروشنده پرداخته و به ارسال‌کننده تعلق دارد؛
        /// فروشنده خودش ارسال می‌کند، پس این مبلغ به سهم او افزوده می‌شود (هرگز کسر نمی‌شود). ریال کامل.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingAmount { get; private set; } = 0;

        /// <summary>
        /// Batch 45 (تصمیم N5): بخشی از <see cref="DiscountAmount"/> که پلتفرم تأمین می‌کند (هزینهٔ کوپن پلتفرم).
        /// از سهم فروشنده کسر نمی‌شود. ریال کامل.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal PlatformDiscountShare { get; private set; } = 0;

        /// <summary>بخشی از تخفیف این گروه که خود فروشنده تأمین می‌کند (کوپن فروشنده).</summary>
        [NotMapped]
        public decimal SellerFundedDiscount => DiscountAmount - PlatformDiscountShare;

        /// <summary>
        /// Batch 45: SubTotal − <see cref="SellerFundedDiscount"/> − CommissionAmount + <see cref="ShippingAmount"/>؛
        /// همان چیزی که به فروشنده می‌رسد. تخفیف کوپن پلتفرم کسر نمی‌شود و ارسال بستانکار می‌شود (FIN-04).
        /// قاعده در <see cref="RialMoney.SellerPayable"/> است. گروه‌های پیش از Batch 45 مقدار ذخیره‌شدهٔ قدیمی را نگه می‌دارند.
        /// </summary>
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

        /// <summary>
        /// Batch 45: نتیجهٔ <c>IPricingService</c> را روی گروه ثبت می‌کند و کمیسیون و سهم فروشنده را با همان قاعدهٔ
        /// <see cref="RialMoney"/> حساب می‌کند. فقط روی گروه Pending (پیش از پرداخت) مجاز است.
        /// </summary>
        /// <param name="discountAmount">سهم گروه از کل تخفیف سفارش (ریال کامل).</param>
        /// <param name="platformDiscountShare">بخشی از همان سهم که پلتفرم تأمین می‌کند (ریال کامل).</param>
        /// <param name="shippingAmount">هزینهٔ ارسال بستهٔ این گروه (ریال کامل).</param>
        public void ApplyPricing(decimal discountAmount, decimal platformDiscountShare, decimal shippingAmount)
        {
            if (Status != SellerGroupStatus.Pending)
                throw new BusinessRuleViolationException($"Pricing can only be applied to a pending group (current: {Status}).", "GROUP_INVALID_STATE");

            if (discountAmount < 0 || platformDiscountShare < 0 || shippingAmount < 0
                || platformDiscountShare > discountAmount || discountAmount > SubTotal
                || !RialMoney.IsWhole(discountAmount) || !RialMoney.IsWhole(platformDiscountShare)
                || !RialMoney.IsWhole(shippingAmount))
                throw new BusinessRuleViolationException("Group pricing amounts are out of range or not whole rials.", "PRICING_INVARIANT_VIOLATED");

            DiscountAmount = discountAmount;
            PlatformDiscountShare = platformDiscountShare;
            ShippingAmount = shippingAmount;
            ShippingCostShare = 0;
            RecalculateSettlement();
        }

        /// <summary>کمیسیون و سهم فروشنده از روی فیلدهای فعلی گروه، با تنها قاعدهٔ پول (<see cref="RialMoney"/>).</summary>
        private void RecalculateSettlement()
        {
            CommissionAmount = RialMoney.Commission(SubTotal - SellerFundedDiscount, CommissionRate);
            SellerPayable = Math.Max(0, RialMoney.SellerPayable(SubTotal, SellerFundedDiscount, CommissionAmount, ShippingAmount));
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
        /// Batch 45: با همان قاعدهٔ <see cref="ApplyPricing"/> دوباره حساب می‌شود؛ برای گروهی که در checkout
        /// قیمت‌گذاری شده نتیجه همان است، و گروهی که قیمت‌گذاری نشده (بدون تخفیف و ارسال) هم درست حساب می‌شود.
        /// </summary>
        public void ApplyCommission()
        {
            if (Status != SellerGroupStatus.Pending)
                throw new BusinessRuleViolationException($"Commission can only be applied to a pending group (current: {Status}).", "GROUP_INVALID_STATE");

            if (CommissionRate < 0 || CommissionRate > 100)
                throw new BusinessRuleViolationException("Commission rate is out of range.", "COMMISSION_RATE_INVALID");

            RecalculateSettlement();
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