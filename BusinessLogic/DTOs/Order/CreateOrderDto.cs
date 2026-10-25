using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Order
{
    public class CreateOrderDto
    {
        public int CustomerId { get; set; }
        public string ShippingFullName { get; set; } = null!;
        public string ShippingAddress { get; set; } = null!;
        public string ShippingPhoneNumber { get; set; } = null!;
        public List<CreateOrderItemDto> Items { get; set; } = new List<CreateOrderItemDto>();
    }
}
