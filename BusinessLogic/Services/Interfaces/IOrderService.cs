using Application.DTOs.Order;
using Application.Entities;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderDto> CreateEmptyOrderAsync(int customerId, string shippingFullName, string shippingAddress, string shippingPhone);
        Task<OrderDto> ConfirmOrderAsync(int orderId);
        Task<IEnumerable<OrderDto>> GetOrdersAsync(int customerId);
    }
}
