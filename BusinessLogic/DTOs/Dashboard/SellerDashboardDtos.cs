namespace BusinessLogic.DTOs.Dashboard
{
    /// <summary>Phase 7 — خلاصه فروش یک فروشنده در یک بازهٔ زمانی.</summary>
    public class SellerSalesSummaryDto
    {
        public int SellerId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>جمع مبلغ فروش (SubTotal گروه‌های فروشنده).</summary>
        public decimal TotalSales { get; set; }

        /// <summary>تعداد سفارش‌های متمایز.</summary>
        public int TotalOrders { get; set; }

        /// <summary>جمع کمیسیون پلتفرم روی این فروش.</summary>
        public decimal CommissionPaid { get; set; }

        /// <summary>آنچه پس از کسر کمیسیون به فروشنده می‌رسد.</summary>
        public decimal NetEarnings { get; set; }

        /// <summary>Phase 8 (R37): مبلغی که تسویه شده و به فروشنده پرداخت شده است.</summary>
        public decimal PaidEarnings { get; set; }

        /// <summary>Phase 8 (R37): مبلغی که تسویه‌اش ثبت شده ولی هنوز پرداخت نشده.</summary>
        public decimal PendingPayouts { get; set; }
    }

    /// <summary>Phase 7 — پرفروش‌ترین واریانت‌های یک فروشنده.</summary>
    public class TopProductDto
    {
        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string? VariantName { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
    }

    /// <summary>Phase 7 — آخرین آیتم‌های سفارش دریافت‌شده توسط فروشنده.</summary>
    public class SellerRecentOrderDto
    {
        public int OrderId { get; set; }
        public int OrderItemId { get; set; }
        public DateTime OrderDate { get; set; }
        public string OrderStatus { get; set; } = null!;
        public string ProductName { get; set; } = null!;
        public string? VariantName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}