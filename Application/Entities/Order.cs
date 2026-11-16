using Application.Common.Helpers;
using Application.Exceptions;
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

        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingCost { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0;

        public int? ShippingMethodId { get; set; }

        [ForeignKey(nameof(ShippingMethodId))]
        public virtual ShippingMethod? ShippingMethod { get; set; }

        public int? CouponId { get; set; }

        [ForeignKey(nameof(CouponId))]
        public virtual Coupon? Coupon { get; set; }

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

        /// <summary>Phase 3: یک گروه به‌ازای هر فروشندهٔ حاضر در سفارش.</summary>
        public virtual ICollection<OrderSellerGroup> SellerGroups { get; set; } = new HashSet<OrderSellerGroup>();
        public virtual Invoice? Invoice { get; set; }
        public virtual ICollection<Payment> Payments { get; set; } = new HashSet<Payment>();

        [MaxLength(100)]
        public string? IdempotencyKey { get; set; }

        public bool CouponQuotaRefunded { get; set; } = false;

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];

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
                throw new BusinessRuleViolationException("Only pending orders can be confirmed.", "ORDER_INVALID_STATE");
            Status = OrderStatus.Processing;
        }

        public void MarkShipped()
        {
            if (Status != OrderStatus.Processing)
                throw new BusinessRuleViolationException("Only processing orders can be marked as shipped.", "ORDER_INVALID_STATE");

            Status = OrderStatus.Shipped;
        }

        /// <summary>Phase 3: تحویل سفارش — پیش‌نیاز ساخت SellerPayout.</summary>
        public void MarkDelivered()
        {
            if (Status != OrderStatus.Processing && Status != OrderStatus.Shipped)
                throw new BusinessRuleViolationException("Only processing or shipped orders can be delivered.", "ORDER_INVALID_STATE");

            Status = OrderStatus.Delivered;
        }

        public void Cancel()
        {
            if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered || Status == OrderStatus.Returned)
                throw new BusinessRuleViolationException("Cannot cancel shipped, delivered, or returned orders.", "ORDER_INVALID_STATE");
            Status = OrderStatus.Cancelled;
        }

        /// <summary>
        /// Phase 9 (R43): برگشت سفارش تحویل‌شده.
        ///
        /// از <see cref="Cancel"/> جدا است چون «لغو» و «مرجوعی» دو رویداد تجاری
        /// متفاوت‌اند: لغو پیش از تحویل، مرجوعی پس از آن. یکی‌کردنشان باعث می‌شد
        /// گزارش «سفارش‌های لغوشده» و «نرخ مرجوعی» قابل تفکیک نباشد.
        /// </summary>
        public void MarkReturned()
        {
            if (Status != OrderStatus.Delivered)
                throw new BusinessRuleViolationException("Only delivered orders can be returned.", "ORDER_INVALID_STATE");

            Status = OrderStatus.Returned;
        }

        /// <summary>
        /// تخفیف و هزینهٔ ارسال محاسبه‌شده توسط <c>IPricingService</c> را روی سفارش ثبت می‌کند.
        /// Batch 45 (FIN-06/FIN-09): هر دو مبلغ ریال کامل و نامنفی‌اند و تخفیف هرگز از جمع اقلام بیشتر نیست؛
        /// در غیر این صورت قیمت‌گذاری خطا دارد و سفارش ثبت نمی‌شود (پیش‌تر کسر ریال بی‌صدا ذخیره می‌شد).
        /// <paramref name="shippingCost"/> جمع هزینهٔ ارسال همهٔ بسته‌هاست (یک هزینه به‌ازای هر فروشنده، تصمیم N4).
        /// </summary>
        public void ApplyDiscountsAndShipping(decimal discount, decimal shippingCost, int? couponId, int? shippingMethodId)
        {
            var itemsTotal = OrderItems.Sum(i => i.Quantity * i.UnitPrice);
            if (discount < 0 || shippingCost < 0 || discount > itemsTotal
                || !RialMoney.IsWhole(discount) || !RialMoney.IsWhole(shippingCost))
                throw new BusinessRuleViolationException("Order discount or shipping is out of range or not whole rials.", "PRICING_INVARIANT_VIOLATED");

            DiscountAmount = discount;
            ShippingCost = shippingCost;
            CouponId = couponId;
            ShippingMethodId = shippingMethodId;
            RecalculateTotal();
        }

        private void RecalculateTotal()
        {
            var itemsTotal = OrderItems.Sum(i => i.Quantity * i.UnitPrice);
            TotalAmount = Math.Max(0, itemsTotal - DiscountAmount + ShippingCost);
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