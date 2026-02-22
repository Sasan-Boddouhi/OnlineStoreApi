using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;

namespace Application.DTOs.Order
{
    public class OrderDto
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public string ShippingFullName { get; set; } = null!;
        public string ShippingAddress { get; set; } = null!;
        public string ShippingPhoneNumber { get; set; } = null!;
        public decimal TotalAmount { get; set; }
        public bool IsConfirmed { get; set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }
}
