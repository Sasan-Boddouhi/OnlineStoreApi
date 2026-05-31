using Application.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using System.Collections.Generic;

namespace BusinessLogic.DTOs.Order
{
    public class OrderDetailsDto : OrderDto
    {
        public new string ShippingFullName { get; set; } = null!;
        public new string ShippingAddress { get; set; } = null!;
        public new string ShippingPhoneNumber { get; set; } = null!;
        public List<OrderItemDto> Items { get; set; } = new();
        public string? InvoiceNumber { get; set; }
        public bool IsPaid { get; set; }
    }
}