using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using System.Linq;

namespace BusinessLogic.Specifications.Orders
{
    public sealed class OrderDetailsSpecification : BaseProjectionSpecification<Order, OrderDetailsDto>
    {
        public OrderDetailsSpecification(int orderId)
        {
            Criteria = o => o.OrderId == orderId;
            Selector = o => new OrderDetailsDto
            {
                OrderId = o.OrderId,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Status = o.Status.ToString(),
                ShippingFullName = o.ShippingFullName,
                ShippingAddress = o.ShippingAddress,
                ShippingPhoneNumber = o.ShippingPhoneNumber,
                Items = o.OrderItems.Select(i => new OrderItemDto
                {
                    OrderItemId = i.OrderItemId,
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    TotalPrice = i.TotalPrice,
                    Description = i.Description
                }).ToList(),
                InvoiceNumber = o.Invoice != null ? o.Invoice.InvoiceNumber : null,
                IsPaid = o.Invoice != null && o.Invoice.Status == InvoiceStatus.Paid
            };
        }
    }
}