using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;

namespace Application.DTOs.Order
{
    public class OrderDto
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
