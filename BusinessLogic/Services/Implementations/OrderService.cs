using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Application.Common.Specifications;
using Application.Options;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;

namespace BusinessLogic.Services.Implementations;

public sealed class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderService> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditService _auditService;
    private readonly CommissionSettings _commissionSettings;

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
        ICurrentUserService currentUserService,
        IInventoryService inventoryService,
        IAuditService auditService,
        IOptions<CommissionSettings> commissionSettings)
    {
        _unitOfWork = unitOfWork;
        _inventoryService = inventoryService;
        _auditService = auditService;
        _commissionSettings = commissionSettings.Value;
        _mapper = mapper;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// سفارش را فقط در صورتی برمی‌گرداند که متعلق به کاربر جاری باشد.
    /// در غیر این صورت NotFoundException پرتاب می‌شود تا وجود سفارش دیگران افشا نشود.
    /// </summary>
    private async Task<Order> GetOwnedOrderAsync(
        int orderId,
        CancellationToken cancellationToken,
        bool includeItems = false)
    {
        var userId = _currentUserService.GetCurrentUserId();

        var customer = await _unitOfWork.Repository<Customer>()
            .FirstOrDefaultAsync(
                new Spec<Customer>().Where(c => c.UserId == userId),
                cancellationToken);

        if (customer is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        // AsTracking: اگر سفارش قبلاً در همین DbContext ردیابی شده باشد، همان نمونه برگردد
        // تا Attach دوباره باعث Identity Conflict نشود.
        var spec = new Spec<Order>()
            .Where(o => o.OrderId == orderId && o.CustomerId == customer.CustomerId)
            .AsTracking();

        if (includeItems)
            spec = spec.Include(o => o.OrderItems).Include(o => o.SellerGroups);

        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);

        if (order is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        return order;
    }

    /// <summary>
    /// نرخ کمیسیون مؤثر: نرخ اختصاصی فروشنده (اگر مقدار دارد)، وگرنه نرخ پیش‌فرض پلتفرم.
    /// </summary>
    private async Task<decimal> ResolveCommissionRateAsync(int sellerId, CancellationToken cancellationToken)
    {
        var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
        return seller?.CommissionRate ?? _commissionSettings.DefaultRate;
    }

    /// <summary>
    /// جمع هر گروه از آیتم‌های کامل سفارش محاسبه می‌شود تا وابسته به بارگذاری بودن
    /// مجموعهٔ Items داخل گروه نباشد.
    /// </summary>
    private static void RecalculateGroupSubTotals(Order order)
    {
        foreach (var group in order.SellerGroups)
            group.RecalculateSubTotal(order.OrderItems);
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

        var order = await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true);

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        // Phase 2 (D2/D5): قیمت از Listing خوانده می‌شود و موجودی از همان Listing کسر می‌گردد.
        // Phase 6: واریانت باید همراه Listing خوانده شود، وگرنه ProductId صفر ثبت می‌شود.
        var listing = await _unitOfWork.Repository<SellerProduct>()
            .FirstOrDefaultAsync(
                new Spec<SellerProduct>()
                    .Where(l => l.ListingId == itemDto.ListingId)
                    .Include(l => l.Variant),
                cancellationToken);

        if (listing == null)
            throw new BusinessException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

        if (listing.Status != SellerProductStatus.Active)
            throw new BusinessException("این پیشنهاد فروش در حال حاضر قابل خرید نیست.", "LISTING_NOT_AVAILABLE");

        await _inventoryService.ReserveAsync(listing.ListingId, itemDto.Quantity, cancellationToken);

        // Phase 6: ProductId از مسیر واریانت گرفته می‌شود و VariantId هم snapshot می‌شود.
        var item = new OrderItem(
            productId: listing.Variant?.ProductId ?? 0,
            quantity: itemDto.Quantity,
            unitPrice: listing.Price,
            description: itemDto.Description
        )
        {
            ListingId = listing.ListingId,
            VariantId = listing.VariantId
        };

        // Phase 3: آیتم‌های یک فروشنده در یک گروه جمع می‌شوند.
        var group = order.SellerGroups.FirstOrDefault(g => g.SellerId == listing.SellerId);
        if (group is null)
        {
            group = new OrderSellerGroup(
                listing.SellerId,
                await ResolveCommissionRateAsync(listing.SellerId, cancellationToken));

            order.SellerGroups.Add(group);
        }

        item.OrderSellerGroup = group;
        order.AddItem(item);
        RecalculateGroupSubTotals(order);

        _unitOfWork.Repository<Order>().Update(order);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException(
                "موجودی به‌طور هم‌زمان توسط درخواست دیگری تغییر کرد. لطفاً دوباره تلاش کنید.",
                "INVENTORY_CONCURRENCY");
        }

        _logger.LogInformation("Item added to order {OrderId}, new total: {TotalAmount}", order.OrderId, order.TotalAmount);
        return _mapper.Map<OrderDto>(order);
    }

    public async Task<OrderDto> AddItemsToOrderAsync(
        int orderId,
        IEnumerable<AddOrderItemDto> itemsDto,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true);

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        foreach (var itemDto in itemsDto)
        {
            var listing = await _unitOfWork.Repository<SellerProduct>()
                .FirstOrDefaultAsync(
                    new Spec<SellerProduct>()
                        .Where(l => l.ListingId == itemDto.ListingId)
                        .Include(l => l.Variant),
                    cancellationToken);

            if (listing == null)
                throw new BusinessException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

            if (listing.Status != SellerProductStatus.Active)
                throw new BusinessException("این پیشنهاد فروش در حال حاضر قابل خرید نیست.", "LISTING_NOT_AVAILABLE");

            await _inventoryService.ReserveAsync(listing.ListingId, itemDto.Quantity, cancellationToken);

            var item = new OrderItem(
                productId: listing.Variant?.ProductId ?? 0,
                quantity: itemDto.Quantity,
                unitPrice: listing.Price,
                description: itemDto.Description
            )
            {
                ListingId = listing.ListingId,
                VariantId = listing.VariantId
            };

            var group = order.SellerGroups.FirstOrDefault(g => g.SellerId == listing.SellerId);
            if (group is null)
            {
                group = new OrderSellerGroup(
                    listing.SellerId,
                    await ResolveCommissionRateAsync(listing.SellerId, cancellationToken));

                order.SellerGroups.Add(group);
            }

            item.OrderSellerGroup = group;
            order.AddItem(item);
        }

        RecalculateGroupSubTotals(order);
        _unitOfWork.Repository<Order>().Update(order);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException(
                "موجودی به‌طور هم‌زمان توسط درخواست دیگری تغییر کرد. لطفاً دوباره تلاش کنید.",
                "INVENTORY_CONCURRENCY");
        }

        _logger.LogInformation("Batch items added to order {OrderId}, new total: {TotalAmount}", order.OrderId, order.TotalAmount);
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

        var order = await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true);

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        var item = order.OrderItems.FirstOrDefault(i => i.OrderItemId == orderItemId);
        if (item == null)
            throw new BusinessException("آیتم مورد نظر در سفارش یافت نشد.", "ORDER_ITEM_NOT_FOUND");

        await _inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);

        order.RemoveItem(item);
        RecalculateGroupSubTotals(order);

        // گروه بدون آیتم نباید باقی بماند (ایندکس یکتای (OrderId, SellerId) هم اجازهٔ تکرار نمی‌دهد).
        foreach (var emptyGroup in order.SellerGroups
                     .Where(g => !order.OrderItems.Any(i => ReferenceEquals(i.OrderSellerGroup, g)))
                     .ToList())
        {
            order.SellerGroups.Remove(emptyGroup);
            _unitOfWork.Repository<OrderSellerGroup>().Delete(emptyGroup);
        }

        _unitOfWork.Repository<Order>().Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<OrderDto>(order);
    }

    #endregion

    #region ConfirmOrder

    public async Task<OrderDto> ConfirmOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Confirming order {OrderId}", orderId);

        var order = await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true);

        order.Confirm();

        // Phase 3: کمیسیون در لحظهٔ تأیید قفل می‌شود (snapshot نرخ).
        foreach (var group in order.SellerGroups)
            group.ApplyCommission();

        _unitOfWork.Repository<Order>().Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<OrderDto>(order);
    }

    #endregion

    #region MarkDelivered

    /// <summary>
    /// Phase 3: تحویل سفارش و ساخت تسویهٔ فروشندگان.
    /// </summary>
    /// <remarks>
    /// این عملیات مدیریتی است و مالکیت مشتری را بررسی نمی‌کند؛
    /// مجوزدهی در لایهٔ Policy انجام می‌شود (AdminOnly).
    /// </remarks>
    public async Task<OrderDto> MarkDeliveredAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Marking order {OrderId} as delivered", orderId);

        var spec = new Spec<Order>()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.SellerGroups)
            .AsTracking();

        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        if (order.Status != OrderStatus.Processing && order.Status != OrderStatus.Shipped)
            throw new BusinessException(
                "فقط سفارش در حال پردازش یا ارسال‌شده قابل تحویل است.", "ORDER_NOT_DELIVERABLE");

        order.MarkDelivered();

        var now = DateTime.UtcNow;

        foreach (var group in order.SellerGroups)
        {
            // Phase 8 (R35): لحظهٔ تحویل ثبت می‌شود تا گزارش ماهانه دقیق باشد.
            group.MarkDelivered(now);

            var payout = new SellerPayout(
                group.SellerId,
                group.OrderSellerGroupId,
                group.SellerPayable,
                order.OrderDate,
                now);

            await _unitOfWork.Repository<SellerPayout>().AddAsync(payout, cancellationToken);
        }

        _unitOfWork.Repository<Order>().Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }

        _logger.LogInformation(
            "Order {OrderId} delivered; {Count} seller payouts created",
            orderId, order.SellerGroups.Count);

        return _mapper.Map<OrderDto>(order);
    }

    #endregion

    #region GetOrders
    public async Task<IEnumerable<OrderDto>> GetOrdersAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving orders for user {UserId}", userId);

        var customer = await _unitOfWork.Repository<Customer>()
            .FirstOrDefaultAsync(
                new Spec<Customer>().Where(c => c.UserId == userId),
                cancellationToken);

        if (customer == null)
            return Enumerable.Empty<OrderDto>();

        var spec = new Spec<Order>()
            .Where(o => o.Customer.UserId == userId)
            .OrderByDescending(o => o.OrderDate);

        var orders = await _unitOfWork.Repository<Order>()
            .ListAsync(spec, cancellationToken);

        return _mapper.Map<IEnumerable<OrderDto>>(orders);
    }

    #endregion

    #region GetOrderDetails

    public async Task<OrderDetailsDto?> GetOrderDetailsAsync(
        int userId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Retrieving order details for user {UserId}, order {OrderId}",
            userId,
            orderId);

        var customer = await _unitOfWork.Repository<Customer>()
            .FirstOrDefaultAsync(
                new Spec<Customer>().Where(c => c.UserId == userId),
                cancellationToken);

        if (customer == null)
            return null;

        var spec = new Spec<Order>()
            .Where(o => o.OrderId == orderId && o.Customer.UserId == userId)
            .Include(o => o.OrderItems)
            .Include(o => o.Invoice);

        var order = await _unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(spec, cancellationToken);

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

        var order = await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true);

        order.Cancel();

        // R6: گروه‌های فروشنده هم باید لغو شوند، وگرنه سفارش لغوشده در گزارش
        // تسویه با وضعیت Processing می‌ماند و پول پرداخت‌نشده نشان می‌دهد.
        foreach (var group in order.SellerGroups)
            group.Cancel();

        foreach (var item in order.OrderItems)
            await _inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);

        // Phase 8 (H): اگر سفارش پرداخت‌شده بود، پول باید برگردد.
        // بدون این، پول مشتری نزد ما می‌ماند و گزارش مالی دروغ می‌گوید.
        await RefundSucceededPaymentsAsync(orderId, cancellationToken);

        // Phase 9 (R40): لغو سفارش یک رویداد حساس است.
        await _auditService.RecordAsync(
            "Order.Cancelled",
            nameof(Order),
            orderId.ToString(),
            $"amount={order.TotalAmount}",
            cancellationToken);

        _unitOfWork.Repository<Order>().Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }

        return _mapper.Map<OrderDto>(order);
    }

    public async Task<OrderDto> MarkReturnedAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Marking order {OrderId} as returned", orderId);

        var spec = new Spec<Order>()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.SellerGroups)
            .AsTracking();

        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        order.MarkReturned();

        foreach (var group in order.SellerGroups)
            group.Cancel();

        foreach (var item in order.OrderItems)
            await _inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);

        var payouts = await _unitOfWork.Repository<SellerPayout>()
            .ListAsync(new Spec<SellerPayout>()
                .Where(p => p.OrderSellerGroup != null && p.OrderSellerGroup.OrderId == orderId && p.Status == PayoutStatus.Pending)
                .AsTracking(),
                cancellationToken);

        foreach (var payout in payouts)
            payout.Cancel();

        await _auditService.RecordAsync(
            "Order.Returned",
            nameof(Order),
            orderId.ToString(),
            $"amount={order.TotalAmount}",
            cancellationToken);

        _unitOfWork.Repository<Order>().Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }

        return _mapper.Map<OrderDto>(order);
    }

    /// <summary>
    /// Phase 8 (H): علامت‌زدن پرداخت‌های موفقِ یک سفارشِ لغوشده به Refunded.
    ///
    /// اینجا عمداً از مخزن استفاده می‌شود و نه از IPaymentService:
    /// PaymentService برای «تأیید سفارش Pending» به OrderService وابسته است؛
    /// اگر OrderService هم به PaymentService وابسته می‌شد، چرخهٔ DI می‌ساختیم.
    /// </summary>
    private async Task RefundSucceededPaymentsAsync(int orderId, CancellationToken cancellationToken)
    {
        var payments = await _unitOfWork.Repository<Payment>()
            .ListAsync(
                new Spec<Payment>()
                    .Where(x => x.OrderId == orderId && x.State == PaymentState.Succeeded)
                    .AsTracking(),
                cancellationToken);

        foreach (var payment in payments)
        {
            payment.TransitionTo(PaymentState.Refunded, note: "Order cancelled after successful payment.");
            await _auditService.RecordAsync(
                "Payment.Refunded",
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"order={orderId} cancelled after payment",
                cancellationToken);
            _unitOfWork.Repository<Payment>().Update(payment);

            _logger.LogInformation(
                "Payment {PaymentId} marked Refunded after cancellation of order {OrderId}",
                payment.PaymentId, orderId);
        }
    }

    #endregion
}