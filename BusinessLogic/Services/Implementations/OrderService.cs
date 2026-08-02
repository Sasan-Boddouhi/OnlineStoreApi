using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Application.Common.Specifications;
using System.Linq.Expressions;
using Application.DTOs.Order;

namespace BusinessLogic.Services.Implementations;

public sealed class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderService> _logger;
    private readonly ICurrentUserService _currentUserService;

    private static readonly Expression<Func<Order, OrderDto>> OrderProjection = o => new OrderDto
    {
        OrderId = o.OrderId,
        OrderDate = o.OrderDate,
        TotalAmount = o.TotalAmount,
        Status = o.Status.ToString(),
        ShippingFullName = o.ShippingFullName,
        ShippingAddress = o.ShippingAddress,
        ShippingPhoneNumber = o.ShippingPhoneNumber
    };

    private static readonly Expression<Func<Order, OrderDetailsDto>> OrderDetailsProjection = o => new OrderDetailsDto
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
            throw new BusinessException("مشتری یافت نشد.", "CUSTOMER_NOT_FOUND");

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

        var orderSpec = new Spec<Order>()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.OrderItems);
        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
        if (order == null)
            throw new BusinessException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(itemDto.ProductId, cancellationToken);
        if (product == null)
            throw new BusinessException("محصول یافت نشد.", "PRODUCT_NOT_FOUND");

        var item = new OrderItem(
            productId: itemDto.ProductId,
            quantity: itemDto.Quantity,
            unitPrice: product.Price,
            description: itemDto.Description
        );
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

        var orderSpec = new Spec<Order>()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.OrderItems);
        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(orderSpec, cancellationToken);
        if (order == null)
            throw new BusinessException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        var item = order.OrderItems.FirstOrDefault(i => i.OrderItemId == orderItemId);
        if (item == null)
            throw new BusinessException("آیتم مورد نظر در سفارش یافت نشد.", "ORDER_ITEM_NOT_FOUND");

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

        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);
        if (order == null)
            throw new BusinessException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        order.Confirm();
        _unitOfWork.Repository<Order>().Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<OrderDto>(order);
    }

    #endregion

    #region GetOrders (برای مشتری خاص)

    public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving orders for customer {CustomerId}", customerId);

        var spec = new Spec<Order>()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate);
        var orders = await _unitOfWork.Repository<Order>().ListAsync(spec, cancellationToken);
        return _mapper.Map<IEnumerable<OrderDto>>(orders);
    }

    #endregion

    #region GetOrderDetails (با آیتم‌ها)

    public async Task<OrderDetailsDto?> GetOrderDetailsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving order details for order {OrderId}", orderId);

        var spec = new Spec<Order>()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Invoice);

        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);
        if (order == null)
            return null;

        return new OrderDetailsDto
        {
            OrderId = order.OrderId,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            ShippingFullName = order.ShippingFullName,
            ShippingAddress = order.ShippingAddress,
            ShippingPhoneNumber = order.ShippingPhoneNumber,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                OrderItemId = i.OrderItemId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice,
                Description = i.Description
            }).ToList(),
            InvoiceNumber = order.Invoice?.InvoiceNumber,
            IsPaid = order.Invoice != null && order.Invoice.Status == InvoiceStatus.Paid
        };
    }

    #endregion

    #region CancelOrder

    public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling order {OrderId}", orderId);

        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);
        if (order == null)
            throw new BusinessException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        order.Cancel();
        _unitOfWork.Repository<Order>().Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<OrderDto>(order);
    }

    #endregion
}