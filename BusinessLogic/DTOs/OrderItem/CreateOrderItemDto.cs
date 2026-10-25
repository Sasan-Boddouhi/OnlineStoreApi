using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.OrderItem
{
    public class CreateOrderItemDto
    {
        public int ProductId { get; set; }

        /// <summary>Phase 3: آیتم باید به یک پیشنهاد فروش مشخص اشاره کند.</summary>
        public int ListingId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public string? Description { get; set; }
    }
}
