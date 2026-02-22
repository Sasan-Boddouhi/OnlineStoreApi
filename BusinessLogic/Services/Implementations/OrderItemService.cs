using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class OrderItemService : IOrderItemService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<OrderItemService> _logger;

        public OrderItemService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<OrderItemService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        #region AddOrderItemAsync
        public async Task<OrderItemDto> AddOrderItemAsync(int orderId, CreateOrderItemDto dto)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId);
            if (order == null) throw new BusinessException("سفارش یافت نشد.");

            var item = new OrderItem
            {
                OrderId = orderId,
                ProductId = dto.ProductId,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice
            };

            await _unitOfWork.Repository<OrderItem>().AddAsync(item);
            order.AddItem(item); // TotalAmount بروزرسانی میشه
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Added item {ProductId} to order {OrderId}", dto.ProductId, orderId);
            return _mapper.Map<OrderItemDto>(item);
        }
        #endregion
    }
}