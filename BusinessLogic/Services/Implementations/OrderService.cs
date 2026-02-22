using Application.DTOs.Order;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Orders;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<OrderService> _logger;

        public OrderService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<OrderService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        #region CreateEmptyOrderAsync

        public async Task<OrderDto> CreateEmptyOrderAsync(int customerId, string shippingFullName, string shippingAddress, string shippingPhone)
        {
            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId);
            if (customer == null)
                throw new BusinessException("کاربر یافت نشد.");

            var order = new Order
            {
                CustomerId = customerId,
                ShippingFullName = shippingFullName,
                ShippingAddress = shippingAddress,
                ShippingPhoneNumber = shippingPhone
            };

            await _unitOfWork.Repository<Order>().AddAsync(order);
            await _unitOfWork.SaveChangesAsync(); // اینجا OrderId گرفته میشه

            _logger.LogInformation("Empty order created with ID {OrderId}", order.OrderId);
            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region ConfirmOrderAsync

        public async Task<OrderDto> ConfirmOrderAsync(int orderId)
        {
            var spec = new OrderByIdWithItemsSpecification(orderId);
            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec);
            if (order == null)
                throw new BusinessException("Order یافت نشد.");

            if (!order.OrderItems.Any())
                throw new BusinessException("سفارش باید حداقل یک آیتم داشته باشد.");

            order.Confirm();
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Order {OrderId} confirmed", orderId);
            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region GetOrders

        public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int customerId)
        {
            var spec = new OrdersByCustomerSpecification(customerId);
            var orders = await _unitOfWork.Repository<Order>().ListAsync(spec);
            return _mapper.Map<IEnumerable<OrderDto>>(orders);
        }

        #endregion
    }
}