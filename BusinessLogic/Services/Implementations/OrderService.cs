using Application.Common.Constants;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Shared;
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
    private readonly IOrderReversalService _orderReversalService;
    private readonly CommissionSettings _commissionSettings;
    private readonly PayoutSettings _payoutSettings;

    private static readonly Expression<Func<Order, OrderDto>> OrderProjection = o => new OrderDto
    {
        OrderId = o.OrderId,
        OrderDate = o.OrderDate,
        TotalAmount = o.TotalAmount,
        Status = o.Status.ToString(),
        ShippingFullName = o.ShippingFullName,
        ShippingAddress = o.ShippingAddress,
        ShippingPhoneNumber = o.ShippingPhoneNumber,
        ShippingCost = o.ShippingCost,
        DiscountAmount = o.DiscountAmount
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
        ShippingCost = o.ShippingCost,
        DiscountAmount = o.DiscountAmount,
        Items = o.OrderItems.Select(i => new OrderItemDto
        {
            OrderItemId = i.OrderItemId,
            ProductId = i.ProductId,
            VariantId = i.VariantId,
            ListingId = i.ListingId,
            ProductName = i.ProductName,
            VariantName = i.VariantName,
            ProductImageUrl = i.ProductImageUrl,
            SellerStoreName = i.SellerStoreName,
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
        IOrderReversalService orderReversalService,
        IOptions<CommissionSettings> commissionSettings,
        IOptions<PayoutSettings>? payoutSettings = null)
    {
        _unitOfWork = unitOfWork;
        _inventoryService = inventoryService;
        _orderReversalService = orderReversalService;
        _commissionSettings = commissionSettings.Value;
        _payoutSettings = payoutSettings?.Value ?? new PayoutSettings();
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
    /// Batch 7 (F-17): ادمین/سوپرادمین می‌تواند هر سفارشی را لغو کند (پشتیبانی به‌جای مشتری)؛
    /// بقیه فقط سفارش خودشان را (<see cref="GetOwnedOrderAsync"/>) و برای سفارش دیگران 404 می‌گیرند.
    /// ادمین هم از گذارهای مجاز پرداخت و سفارش (مثلاً سفارش ارسال‌شده لغو نمی‌شود) عبور نمی‌کند.
    /// </summary>
    private async Task<(Order Order, bool IsAdmin)> GetCancellableOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var role = _currentUserService.GetCurrentUserRole();
        var isAdmin = string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
            return (await GetOwnedOrderAsync(orderId, cancellationToken, includeItems: true), false);

        var order = await _unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>()
                    .Where(o => o.OrderId == orderId)
                    .Include(o => o.OrderItems)
                    .Include(o => o.SellerGroups)
                    .AsTracking(),
                cancellationToken);

        if (order is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        return (order, true);
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
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating empty order for customer {CustomerId}", customerId);

        var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId, cancellationToken);
        if (customer == null)
            throw new NotFoundException("مشتری یافت نشد.", "CUSTOMER_NOT_FOUND");

        var order = new Order
        {
            CustomerId = customerId,
            ShippingFullName = shippingFullName,
            ShippingAddress = shippingAddress,
            ShippingPhoneNumber = shippingPhone,
            IdempotencyKey = idempotencyKey,
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
            throw new ConflictException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        // Phase 2 (D2/D5): قیمت از Listing خوانده می‌شود و موجودی از همان Listing کسر می‌گردد.
        // Phase 6: واریانت باید همراه Listing خوانده شود، وگرنه ProductId صفر ثبت می‌شود.
        var listing = await _unitOfWork.Repository<SellerProduct>()
            .FirstOrDefaultAsync(
                new Spec<SellerProduct>()
                    .Where(l => l.ListingId == itemDto.ListingId)
                    .Include(l => l.Variant)
                    .Include(l => l.Seller),
                cancellationToken);

        if (listing == null)
            throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

        var product = listing.Variant != null && listing.Variant.ProductId > 0
            ? await _unitOfWork.Repository<Product>().GetByIdAsync(listing.Variant.ProductId, cancellationToken)
            : null;

        // Batch 6 (F-15): محصول یا واریانت حذف‌شده (غیرفعال) قابل خرید نیست.
        if (listing.Status != SellerProductStatus.Active || !IsCatalogEntryActive(listing, product))
            throw new ConflictException("این پیشنهاد فروش در حال حاضر قابل خرید نیست.", "LISTING_NOT_AVAILABLE");

        await _inventoryService.ReserveAsync(listing.ListingId, itemDto.Quantity, cancellationToken);

        // Phase 6 & M7.9: ProductId از مسیر واریانت گرفته می‌شود و مشخصات هم snapshot می‌شود.
        var item = new OrderItem(
            productId: listing.Variant?.ProductId ?? 0,
            quantity: itemDto.Quantity,
            unitPrice: listing.Price,
            description: itemDto.Description
        )
        {
            ListingId = listing.ListingId,
            VariantId = listing.VariantId,
            ProductName = product?.Name,
            VariantName = listing.Variant?.Name ?? listing.Variant?.Sku,
            ProductImageUrl = product?.ImageUrl,
            SellerStoreName = listing.Seller?.StoreName
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
            throw new ConflictException(
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
            throw new ConflictException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        var itemsList = itemsDto.ToList();
        var listingIds = itemsList.Select(i => i.ListingId).Distinct().ToList();

        // M7.12: Batch pre-fetch all listings, variants, sellers, and products to eliminate N+1 queries
        var listings = (await _unitOfWork.Repository<SellerProduct>()
            .ListAsync(
                new Spec<SellerProduct>()
                    .Where(l => listingIds.Contains(l.ListingId))
                    .Include(l => l.Variant)
                    .Include(l => l.Seller),
                cancellationToken))
            .ToDictionary(l => l.ListingId);

        var productIds = listings.Values
            .Where(l => l.Variant != null && l.Variant.ProductId > 0)
            .Select(l => l.Variant!.ProductId)
            .Distinct()
            .ToList();

        var products = productIds.Count > 0
            ? (await _unitOfWork.Repository<Product>()
                .ListAsync(new Spec<Product>().Where(p => productIds.Contains(p.ProductId)), cancellationToken))
                .ToDictionary(p => p.ProductId)
            : new Dictionary<int, Product>();

        var sellerIds = listings.Values.Select(l => l.SellerId).Distinct().ToList();
        var sellers = (await _unitOfWork.Repository<Seller>()
            .ListAsync(new Spec<Seller>().Where(s => sellerIds.Contains(s.SellerId)), cancellationToken))
            .ToDictionary(s => s.SellerId);

        foreach (var itemDto in itemsList)
        {
            if (!listings.TryGetValue(itemDto.ListingId, out var listing))
                throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

            var product = (listing.Variant != null && listing.Variant.ProductId > 0 && products.TryGetValue(listing.Variant.ProductId, out var prod))
                ? prod
                : null;

            // Batch 6 (F-15): همان قاعدهٔ CartService؛ محصول یا واریانت غیرفعال قابل خرید نیست.
            if (listing.Status != SellerProductStatus.Active ||
                listing.Seller == null ||
                listing.Seller.Status != SellerStatus.Approved ||
                !listing.Seller.IsActive ||
                !IsCatalogEntryActive(listing, product))
                throw new ConflictException("این پیشنهاد فروش در حال حاضر قابل خرید نیست.", "LISTING_NOT_AVAILABLE");

            await _inventoryService.ReserveAsync(listing.ListingId, itemDto.Quantity, cancellationToken);

            var item = new OrderItem(
                productId: listing.Variant?.ProductId ?? 0,
                quantity: itemDto.Quantity,
                unitPrice: listing.Price,
                description: itemDto.Description
            )
            {
                ListingId = listing.ListingId,
                VariantId = listing.VariantId,
                ProductName = product?.Name,
                VariantName = listing.Variant?.Name ?? listing.Variant?.Sku,
                ProductImageUrl = product?.ImageUrl,
                SellerStoreName = listing.Seller?.StoreName
            };

            var group = order.SellerGroups.FirstOrDefault(g => g.SellerId == listing.SellerId);
            if (group is null)
            {
                var commissionRate = sellers.TryGetValue(listing.SellerId, out var sellerEntity) && sellerEntity.CommissionRate.HasValue
                    ? sellerEntity.CommissionRate.Value
                    : _commissionSettings.DefaultRate;

                group = new OrderSellerGroup(listing.SellerId, commissionRate);
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
            throw new ConflictException(
                "موجودی به‌طور هم‌زمان توسط درخواست دیگری تغییر کرد. لطفاً دوباره تلاش کنید.",
                "INVENTORY_CONCURRENCY");
        }

        _logger.LogInformation("Batch items added to order {OrderId}, new total: {TotalAmount}", order.OrderId, order.TotalAmount);
        return _mapper.Map<OrderDto>(order);
    }

    /// <summary>
    /// Batch 6 (F-15): واریانت و محصولِ پیشنهاد فروش باید فعال باشند.
    /// واریانت یا محصولِ لودنشده (null) هم قابل خرید شمرده نمی‌شود.
    /// </summary>
    private static bool IsCatalogEntryActive(SellerProduct listing, Product? product)
        => listing.Variant is { IsActive: true } && product is { IsActive: true };

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
            throw new ConflictException("فقط سفارش‌های در انتظار قابل ویرایش هستند.", "ORDER_NOT_PENDING");

        var item = order.OrderItems.FirstOrDefault(i => i.OrderItemId == orderItemId);
        if (item == null)
            throw new NotFoundException("آیتم مورد نظر در سفارش یافت نشد.", "ORDER_ITEM_NOT_FOUND");

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

        // Batch 4 (F-02): همان گارد مسیر فروشنده — سفارش باید پرداخت‌شده و لغو/مرجوع/برگشت‌وجه‌نشده باشد.
        await OrderFulfilmentGuard.EnsureOrderIsPaidAsync(_unitOfWork, order, cancellationToken);

        if (order.Status != OrderStatus.Processing && order.Status != OrderStatus.Shipped)
            throw new ConflictException(
                "فقط سفارش در حال پردازش یا ارسال‌شده قابل تحویل است.", "ORDER_NOT_DELIVERABLE");

        // همهٔ بسته‌های باز باید پیش از تحویل ارسال شده باشند؛ پیش از هر تغییری بررسی می‌شود.
        var openGroups = order.SellerGroups
            .Where(g => g.Status != SellerGroupStatus.Delivered && g.Status != SellerGroupStatus.Cancelled)
            .ToList();

        foreach (var group in openGroups)
            OrderFulfilmentGuard.EnsureGroupCanBeDelivered(group);

        order.MarkDelivered();

        var now = DateTime.UtcNow;

        foreach (var group in openGroups)
            group.MarkDelivered(now);

        var payoutGroupIds = new List<int>();

        foreach (var group in order.SellerGroups.Where(g => g.Status == SellerGroupStatus.Delivered))
        {
            if (await OrderFulfilmentGuard.AddPayoutIfMissingAsync(_unitOfWork, group, order.OrderDate, now, _payoutSettings.HoldDays, cancellationToken))
                payoutGroupIds.Add(group.OrderSellerGroupId);
        }

        _unitOfWork.Repository<Order>().Update(order);
        await OrderFulfilmentGuard.SaveAsync(_unitOfWork, payoutGroupIds, cancellationToken);

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

        var role = _currentUserService.GetCurrentUserRole();
        var isAdmin = string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        Spec<Order> spec;
        if (isAdmin)
        {
            spec = new Spec<Order>()
                .Where(o => o.OrderId == orderId)
                .Include(o => o.OrderItems)
                .Include(o => o.Invoice);
        }
        else
        {
            var customer = await _unitOfWork.Repository<Customer>()
                .FirstOrDefaultAsync(
                    new Spec<Customer>().Where(c => c.UserId == userId),
                    cancellationToken);

            if (customer == null)
                return null;

            spec = new Spec<Order>()
                .Where(o => o.OrderId == orderId && o.Customer.UserId == userId)
                .Include(o => o.OrderItems)
                .Include(o => o.Invoice);
        }

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
            ShippingCost = order.ShippingCost,
            DiscountAmount = order.DiscountAmount,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                OrderItemId = i.OrderItemId,
                ProductId = i.ProductId,
                VariantId = i.VariantId,
                ListingId = i.ListingId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                ProductImageUrl = i.ProductImageUrl,
                SellerStoreName = i.SellerStoreName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice,
                Description = i.Description
            }).ToList(),
            InvoiceNumber = order.Invoice?.InvoiceNumber,
            IsPaid = order.Invoice != null && order.Invoice.Status == InvoiceStatus.Paid
        };
    }

    public async Task<PagedResult<OrderDto>> GetAdminOrdersAsync(
        OrderStatus? status,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var spec = new Spec<Order>().OrderByDescending(o => o.OrderDate);
        if (status.HasValue)
            spec.Where(o => o.Status == status.Value);

        var totalCount = await _unitOfWork.Repository<Order>().CountAsync(spec, cancellationToken);

        spec.ApplyPaging((pageNumber - 1) * pageSize, pageSize);

        var orders = await _unitOfWork.Repository<Order>().ListAsync(spec, cancellationToken);

        return new PagedResult<OrderDto>
        {
            Items = _mapper.Map<List<OrderDto>>(orders),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    #endregion

    #region CancelOrder

    /// <summary>
    /// لغو سفارش. Batch 41 (FIN-01، FIN-03): همهٔ اثرها (گروه‌ها، موجودی، سهمیهٔ کوپن، پرداخت‌های باز، درخواست
    /// برگشت وجه) در <see cref="IOrderReversalService"/> نوشته شده‌اند. پرداخت موفقِ سفارش لغوشده RefundPending
    /// می‌شود، نه Refunded: پول هنوز برنگشته است. مشتری پس از ارسال یا تحویل حتی یک بسته 409 ORDER_PARTLY_SHIPPED می‌گیرد.
    /// </summary>
    public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling order {OrderId}", orderId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var (order, isAdmin) = await GetCancellableOrderAsync(orderId, cancellationToken);

            await _orderReversalService.ReverseAsync(
                order,
                isAdmin ? OrderReversalKind.AdminCancel : OrderReversalKind.CustomerCancel,
                isAdmin ? "order cancelled by admin" : "order cancelled by customer",
                cancellationToken);

            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return _mapper.Map<OrderDto>(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }
        catch (Exception)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// مرجوع کردن سفارش تحویل‌شده توسط ادمین. Batch 41 (FIN-01): از همان روال برگرداندن سفارش می‌گذرد؛ بسته‌ها
    /// Returned، موجودی آزاد، تسویه‌های Pending لغو، برای تسویه‌های Paid بدهی بازپس‌گیری ثبت و پرداخت موفق
    /// RefundPending می‌شود (پیش‌تر مرجوعی هیچ برگشت وجهی ثبت نمی‌کرد).
    /// </summary>
    public async Task<OrderDto> MarkReturnedAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Marking order {OrderId} as returned", orderId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var spec = new Spec<Order>()
                .Where(o => o.OrderId == orderId)
                .Include(o => o.OrderItems)
                .Include(o => o.SellerGroups)
                .AsTracking();

            var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);
            if (order is null)
                throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

            await _orderReversalService.ReverseAsync(
                order, OrderReversalKind.AdminReturn, "order returned", cancellationToken);

            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return _mapper.Map<OrderDto>(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    #endregion
}