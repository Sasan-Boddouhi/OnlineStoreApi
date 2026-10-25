using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.OrderItem
{
    public class AddOrderItemDto
    {
        /// <summary>Phase 2 (D5): آیتم سفارش به یک پیشنهاد فروش (Listing) اشاره می‌کند، نه مستقیماً به محصول.</summary>
        public int ListingId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Description { get; set; }
    }
}
