using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.OrderItem
{
    public class OrderItemDto
    {
        public int OrderItemId { get; set; }
        public int ProductId { get; set; }

        /// <summary>Phase 6: واریانتِ خریداری‌شده در لحظهٔ ثبت سفارش.</summary>
        public int? VariantId { get; set; }

        /// <summary>Phase 2/D5: پیشنهاد فروشی که سفارش از آن ثبت شده است.</summary>
        public int ListingId { get; set; }
        public string? ProductName { get; set; }
        public string? VariantName { get; set; }
        public string? ProductImageUrl { get; set; }
        public string? SellerStoreName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; } 
        public string? Description { get; set; }
    }
}
