using System;

namespace Application.DTOs.Order
{
    public class OrderDto
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShippingFullName { get; internal set; } = null!;
        public string ShippingAddress { get; internal set; } = null!;
        public string ShippingPhoneNumber { get; internal set; } = null!;
    }
}