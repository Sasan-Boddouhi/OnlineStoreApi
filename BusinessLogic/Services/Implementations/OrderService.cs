using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Orders;
using Microsoft.Extensions.Logging;
using Application.Common.Helpers;
using Application.Common.Specifications;
using Application.DTOs.Order;

namespace BusinessLogic.Services.Implementations
{
    public sealed class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<OrderService> _logger;
        private readonly ICurrentUserService _currentUserService;

        public OrderService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ILogger<OrderService> logger,
            ICurrentUserService currentUserService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
            _currentUserService = currentUserService;
        }

        #region CreateEmptyOrder

        public async Task<OrderDto> CreateEmptyOrderAsync(
            int customerId,
            string shippingFullName,
            string shippingAddress,
            string shippingPhone,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating empty order for customer {CustomerId}", customerId);

            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
                throw new BusinessException("مشتری یافت نشد.");

            var order = new Order
            {
                CustomerId = customerId,
                ShippingFullName = shippingFullName,
                ShippingAddress = shippingAddress,
                ShippingPhoneNumber = shippingPhone,
                OrderDate = DateTime.UtcNow
            };

            await _unitOfWork.Repository<Order>().AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Empty order created with ID {OrderId}", order.OrderId);
            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region AddItemToOrder

        public async Task<OrderDto> AddItemToOrderAsync(
            int orderId,
            AddOrderItemDto itemDto,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Adding item to order {OrderId}", orderId);

            var orderSpec = new OrderByIdWithItemsSpecification(orderId);
            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
            if (order == null)
                throw new BusinessException("سفارش یافت نشد.");

            if (order.Status != OrderStatus.Pending)
                throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.");

            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(itemDto.ProductId, cancellationToken);
            if (product == null)
                throw new BusinessException("محصول یافت نشد.");

            var item = new OrderItem(
                productId: itemDto.ProductId,
                quantity: itemDto.Quantity,
                unitPrice: product.Price, // یا itemDto.UnitPrice
                description: itemDto.Description
            );
            // محاسبه TotalPrice داخل AddItem انجام می‌شود
            order.AddItem(item);

            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Item added to order {OrderId}, new total: {TotalAmount}", order.OrderId, order.TotalAmount);
            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region RemoveItemFromOrder

        public async Task<OrderDto> RemoveItemFromOrderAsync(
            int orderId,
            int orderItemId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Removing item {OrderItemId} from order {OrderId}", orderItemId, orderId);

            var orderSpec = new OrderByIdWithItemsSpecification(orderId);
            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
            if (order == null)
                throw new BusinessException("سفارش یافت نشد.");

            if (order.Status != OrderStatus.Pending)
                throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.");

            var item = order.OrderItems.FirstOrDefault(i => i.OrderItemId == orderItemId);
            if (item == null)
                throw new BusinessException("آیتم مورد نظر در سفارش یافت نشد.");

            order.RemoveItem(item);

            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region ConfirmOrder

        public async Task<OrderDto> ConfirmOrderAsync(int orderId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Confirming order {OrderId}", orderId);

            var orderSpec = new OrderByIdWithItemsSpecification(orderId);
            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
            if (order == null)
                throw new BusinessException("سفارش یافت نشد.");

            order.Confirm();
            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return _mapper.Map<OrderDto>(order);
        }

        #endregion

        #region GetOrders

        public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int customerId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving orders for customer {CustomerId}", customerId);

            var spec = new OrdersByCustomerSpecification(customerId);
            var orders = await _unitOfWork.Repository<Order>().ListAsync(spec, cancellationToken);
            return _mapper.Map<IEnumerable<OrderDto>>(orders);
        }

        #endregion

        #region GetOrderDetails

        public async Task<OrderDetailsDto?> GetOrderDetailsAsync(int orderId, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving order details for order {OrderId}", orderId);

            var spec = new OrderDetailsSpecification(orderId);
            return await _unitOfWork.Repository<Order>().FirstOrDefaultAsync<OrderDetailsDto>(spec, cancellationToken);
        }

        #endregion

        #region CancelOrder

        public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Cancelling order {OrderId}", orderId);

            var orderSpec = new OrderByIdWithItemsSpecification(orderId);
            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
            if (order == null)
                throw new BusinessException("سفارش یافت نشد.");

            order.Cancel(); // متد Cancel را قبلاً در Entity اضافه کردیم
            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return _mapper.Map<OrderDto>(order);
        }

        #endregion
    }
}