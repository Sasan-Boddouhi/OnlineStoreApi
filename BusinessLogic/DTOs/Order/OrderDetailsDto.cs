using BusinessLogic.DTOs.OrderItem;
using System.Collections.Generic;

namespace BusinessLogic.DTOs.Order
{
    public class OrderDetailsDto : OrderDto
    {
        public List<OrderItemDto> Items { get; set; } = new();
        public string? InvoiceNumber { get; set; }
        public bool IsPaid { get; set; }
    }
}