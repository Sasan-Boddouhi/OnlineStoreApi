using Application.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Order
{
    public class OrderDetailsDto : OrderDto
    {
        public string ShippingFullName { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string ShippingPhoneNumber { get; set; } = string.Empty;
        public List<OrderItemDto> Items { get; set; } = new();
        public string? InvoiceNumber { get; set; }
        public bool IsPaid { get; set; }
    }
}
