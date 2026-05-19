using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IOrderItemService
    {
        Task<OrderItemDto> AddOrderItemAsync(int orderId, CreateOrderItemDto dto, CancellationToken cancellationToken = default);
    }
}
