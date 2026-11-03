using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    /// <summary>
    /// Phase 4 — سبد خرید و نهایی‌سازی سفارش.
    /// </summary>
    public sealed class CartService : ICartService
    {
        /// <summary>سقف تعداد هر قلم در سبد؛ جلوی ورودی‌های مخرب/اشتباه را می‌گیرد.</summary>
        private const decimal MaxLineQuantity = 1000m;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<CartService> _logger;
        private readonly IInventoryService _inventoryService;
        private readonly IOrderService _orderService;
        private readonly IMapper _mapper;

        public CartService(
            IUnitOfWork unitOfWork,
            ILogger<CartService> logger,
            IInventoryService inventoryService,
            IOrderService orderService,
            IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _inventoryService = inventoryService;
            _orderService = orderService;
            _mapper = mapper;
        }

        public CartService(
            IUnitOfWork unitOfWork,
            ILogger<CartService> logger,
            IInventoryService inventoryService,
            IOrderService orderService)
            : this(unitOfWork, logger, inventoryService, orderService, null!)
        {
        }

        // ==================== ابزارهای داخلی ====================

        private static void EnsureOwner(int? userId, string? sessionId)
        {
            if (!userId.HasValue && string.IsNullOrWhiteSpace(sessionId))
                throw new BusinessException(
                    "برای استفاده از سبد خرید یا وارد شوید یا شناسهٔ نشست را بفرستید.",
                    "CART_OWNER_REQUIRED");
        }

        private async Task<Cart?> FindCartAsync(
            int? userId,
            string? sessionId,
            bool tracking,
            CancellationToken cancellationToken)
        {
            var spec = new Spec<Cart>().Include(c => c.Items);

            if (userId.HasValue)
                spec.Where(c => c.UserId == userId.Value);
            else
                spec.Where(c => c.SessionId == sessionId);

            if (tracking)
                spec.AsTracking();

            return await _unitOfWork.Repository<Cart>().FirstOrDefaultAsync(spec, cancellationToken);
        }

        private async Task<Cart> GetOrCreateCartAsync(
            int? userId,
            string? sessionId,
            CancellationToken cancellationToken)
        {
            EnsureOwner(userId, sessionId);

            var existing = await FindCartAsync(userId, sessionId, tracking: true, cancellationToken);
            if (existing is not null)
                return existing;

            var cart = new Cart
            {
                UserId = userId,
                SessionId = userId.HasValue ? null : sessionId
            };

            await _unitOfWork.Repository<Cart>().AddAsync(cart, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cart {CartId} created for {Owner}", cart.CartId, userId.HasValue ? $"user {userId}" : $"session {sessionId}");
            return cart;
        }

        /// <summary>
        /// Listingها جداگانه بارگذاری می‌شوند تا به ThenInclude نیاز نباشد
        /// (Spec فعلی فقط Include تک‌سطحی دارد) و دو کوئری ساده و قابل پیش‌بینی بمانیم.
        /// </summary>
        private async Task<Dictionary<int, SellerProduct>> LoadListingsAsync(
            IEnumerable<int> listingIds,
            CancellationToken cancellationToken)
        {
            var ids = listingIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<int, SellerProduct>();

            // Phase 6: Listing به Variant وصل است، نه به Product.
            // Spec فقط include یک‌سطحی دارد، پس محصول از طریق واریانت در کوئری دوم خوانده می‌شود.
            var spec = new Spec<SellerProduct>()
                .Where(l => ids.Contains(l.ListingId))
                .Include(l => l.Variant)
                .Include(l => l.Seller);

            var listings = await _unitOfWork.Repository<SellerProduct>()
                .ListAsync(spec, cancellationToken);

            return listings.ToDictionary(l => l.ListingId);
        }

        /// <summary>
        /// نام محصول از واریانتِ هر Listing استخراج و در یک کوئری جدا خوانده می‌شود.
        /// </summary>
        private async Task<Dictionary<int, (string Name, string? ImageUrl)>> LoadProductInfoAsync(
            Dictionary<int, SellerProduct> listings,
            CancellationToken cancellationToken)
        {
            var productIds = listings.Values
                .Select(l => l.Variant?.ProductId ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (productIds.Count == 0)
                return new Dictionary<int, (string Name, string? ImageUrl)>();

            var spec = new Spec<Product>().Where(p => productIds.Contains(p.ProductId));
            var products = await _unitOfWork.Repository<Product>().ListAsync(spec, cancellationToken);

            return products.ToDictionary(p => p.ProductId, p => (p.Name, p.ImageUrl));
        }

        private async Task<Dictionary<int, string>> LoadProductNamesAsync(
            Dictionary<int, SellerProduct> listings,
            CancellationToken cancellationToken)
        {
            var info = await LoadProductInfoAsync(listings, cancellationToken);
            return info.ToDictionary(k => k.Key, v => v.Value.Name);
        }

        private async Task<SellerProduct> LoadActiveListingAsync(int listingId, CancellationToken cancellationToken)
        {
            var listing = await _unitOfWork.Repository<SellerProduct>()
                .FirstOrDefaultAsync(
                    new Spec<SellerProduct>()
                        .Where(l => l.ListingId == listingId)
                        .Include(l => l.Seller),
                    cancellationToken);

            if (listing is null)
                throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

            if (listing.Status != SellerProductStatus.Active ||
                listing.Seller == null ||
                listing.Seller.Status != SellerStatus.Approved ||
                !listing.Seller.IsActive)
                throw new BusinessException("این پیشنهاد فروش در حال حاضر فعال نیست.", "LISTING_NOT_ACTIVE");

            return listing;
        }

        private async Task EnsureStockAsync(int listingId, decimal quantity, CancellationToken cancellationToken)
        {
            var listing = await _unitOfWork.Repository<SellerProduct>()
                .FirstOrDefaultAsync(
                    new Spec<SellerProduct>()
                        .Where(l => l.ListingId == listingId)
                        .Include(l => l.Seller),
                    cancellationToken);

            if (listing is null ||
                listing.Status != SellerProductStatus.Active ||
                listing.Seller == null ||
                listing.Seller.Status != SellerStatus.Approved ||
                !listing.Seller.IsActive)
            {
                throw new BusinessException("این پیشنهاد فروش در حال حاضر فعال نیست.", "LISTING_NOT_ACTIVE");
            }

            var available = await _inventoryService.GetAvailableAsync(listingId, cancellationToken);
            if (available < quantity)
                throw new BusinessException("موجودی کافی نیست.", "INSUFFICIENT_STOCK");
        }

        /// <summary>
        /// قیمت نمایشی سبد از Listing فعلی خوانده می‌شود، نه از snapshot قدیمی؛
        /// وگرنه کاربر قیمتی در سبد می‌دید و در سفارش قیمت دیگری پرداخت می‌کرد.
        /// UnitPrice در CartItem فقط سابقهٔ قیمت هنگام افزودن است.
        /// </summary>
        private async Task<CartDto> MapCartAsync(Cart cart, CancellationToken cancellationToken)
        {
            var listings = await LoadListingsAsync(cart.Items.Select(i => i.ListingId), cancellationToken);
            var productInfo = await LoadProductInfoAsync(listings, cancellationToken);

            var items = cart.Items
                .OrderBy(i => i.CartItemId)
                .Select(i =>
                {
                    listings.TryGetValue(i.ListingId, out var listing);
                    var unitPrice = listing?.Price ?? i.UnitPrice;
                    var prodId = listing?.Variant?.ProductId ?? 0;
                    productInfo.TryGetValue(prodId, out var pInfo);

                    return new CartItemDto
                    {
                        CartItemId = i.CartItemId,
                        ListingId = i.ListingId,
                        SellerId = listing?.SellerId ?? 0,
                        SellerStoreName = listing?.Seller?.StoreName ?? string.Empty,
                        ProductName = pInfo.Name ?? string.Empty,
                        ProductImageUrl = pInfo.ImageUrl,
                        // Phase 7: اگر نام واریانت خالی بود، SKU و در نهایت خالی.
                        VariantName = listing?.Variant?.Name ?? listing?.Variant?.Sku,
                        UnitPrice = unitPrice,
                        Quantity = i.Quantity,
                        LineTotal = unitPrice * i.Quantity
                    };
                })
                .ToList();

            if (_mapper != null)
            {
                var cartDto = _mapper.Map<CartDto>(cart);
                cartDto.Items = items;
                cartDto.TotalItems = items.Count;
                cartDto.TotalAmount = items.Sum(i => i.LineTotal);
                return cartDto;
            }

            return new CartDto
            {
                CartId = cart.CartId,
                IsAnonymous = cart.IsAnonymous,
                Items = items,
                TotalItems = items.Count,
                TotalAmount = items.Sum(i => i.LineTotal)
            };
        }

        // ==================== عملیات سبد ====================

        public async Task<CartDto> GetCartAsync(
            int? userId,
            string? sessionId,
            CancellationToken cancellationToken = default)
        {
            var cart = await GetOrCreateCartAsync(userId, sessionId, cancellationToken);
            return await MapCartAsync(cart, cancellationToken);
        }

        public async Task<CartDto> AddItemAsync(
            int? userId,
            string? sessionId,
            AddCartItemDto dto,
            CancellationToken cancellationToken = default)
        {
            if (dto.Quantity <= 0)
                throw new BusinessException("تعداد باید بزرگ‌تر از صفر باشد.", "INVALID_QUANTITY");

            var listing = await LoadActiveListingAsync(dto.ListingId, cancellationToken);
            var cart = await GetOrCreateCartAsync(userId, sessionId, cancellationToken);

            var existing = cart.Items.FirstOrDefault(i => i.ListingId == dto.ListingId);
            var newQuantity = (existing?.Quantity ?? 0) + dto.Quantity;

            if (newQuantity > MaxLineQuantity)
                throw new BusinessException($"حداکثر تعداد هر قلم {MaxLineQuantity} است.", "QUANTITY_LIMIT_EXCEEDED");

            await EnsureStockAsync(dto.ListingId, newQuantity, cancellationToken);

            if (existing is null)
            {
                var item = new CartItem
                {
                    CartId = cart.CartId,
                    ListingId = listing.ListingId,
                    // Phase 6: واریانت از روی Listing snapshot می‌شود.
                    VariantId = listing.VariantId,
                    Quantity = dto.Quantity,
                    UnitPrice = listing.Price,
                    AddedAt = DateTime.UtcNow
                };

                await _unitOfWork.Repository<CartItem>().AddAsync(item, cancellationToken);
                cart.Items.Add(item);
            }
            else
            {
                existing.Quantity = newQuantity;
                _unitOfWork.Repository<CartItem>().Update(existing);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Listing {ListingId} x{Quantity} added to cart {CartId}",
                dto.ListingId, dto.Quantity, cart.CartId);

            return await MapCartAsync(cart, cancellationToken);
        }

        public async Task<CartDto> UpdateQuantityAsync(
            int? userId,
            string? sessionId,
            int listingId,
            decimal quantity,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0)
                return await RemoveItemAsync(userId, sessionId, listingId, cancellationToken);

            if (quantity > MaxLineQuantity)
                throw new BusinessException($"حداکثر تعداد هر قلم {MaxLineQuantity} است.", "QUANTITY_LIMIT_EXCEEDED");

            var cart = await FindCartAsync(userId, sessionId, tracking: true, cancellationToken);
            var item = cart?.Items.FirstOrDefault(i => i.ListingId == listingId);

            if (cart is null || item is null)
                throw new NotFoundException("این قلم در سبد خرید نیست.", "CART_ITEM_NOT_FOUND");

            await EnsureStockAsync(listingId, quantity, cancellationToken);

            item.Quantity = quantity;
            _unitOfWork.Repository<CartItem>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await MapCartAsync(cart, cancellationToken);
        }

        public async Task<CartDto> RemoveItemAsync(
            int? userId,
            string? sessionId,
            int listingId,
            CancellationToken cancellationToken = default)
        {
            var cart = await FindCartAsync(userId, sessionId, tracking: true, cancellationToken);
            var item = cart?.Items.FirstOrDefault(i => i.ListingId == listingId);

            if (cart is null || item is null)
                throw new NotFoundException("این قلم در سبد خرید نیست.", "CART_ITEM_NOT_FOUND");

            _unitOfWork.Repository<CartItem>().Delete(item);
            cart.Items.Remove(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await MapCartAsync(cart, cancellationToken);
        }

        public async Task ClearCartAsync(
            int? userId,
            string? sessionId,
            CancellationToken cancellationToken = default)
        {
            var cart = await FindCartAsync(userId, sessionId, tracking: true, cancellationToken);
            if (cart is null || cart.Items.Count == 0)
                return;

            foreach (var item in cart.Items.ToList())
            {
                _unitOfWork.Repository<CartItem>().Delete(item);
                cart.Items.Remove(item);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cart {CartId} cleared", cart.CartId);
        }

        public async Task<CartDto> MergeAnonymousCartAsync(
            string sessionId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return await GetCartAsync(userId, null, cancellationToken);

            var anonymousCart = await FindCartAsync(null, sessionId, tracking: true, cancellationToken);
            if (anonymousCart is null || anonymousCart.Items.Count == 0)
                return await GetCartAsync(userId, null, cancellationToken);

            var userCart = await GetOrCreateCartAsync(userId, null, cancellationToken);

            // ابتدا همه را اعتبارسنجی می‌کنیم؛ اگر یکی ناموجود بود، هیچ‌چیز تغییر نمی‌کند.
            var planned = anonymousCart.Items
                .Select(i => new
                {
                    i.ListingId,
                    Quantity = i.Quantity + (userCart.Items.FirstOrDefault(u => u.ListingId == i.ListingId)?.Quantity ?? 0)
                })
                .ToList();

            foreach (var entry in planned)
            {
                if (entry.Quantity > MaxLineQuantity)
                    throw new BusinessException($"حداکثر تعداد هر قلم {MaxLineQuantity} است.", "QUANTITY_LIMIT_EXCEEDED");

                await EnsureStockAsync(entry.ListingId, entry.Quantity, cancellationToken);
            }

            foreach (var item in anonymousCart.Items.ToList())
            {
                var existing = userCart.Items.FirstOrDefault(u => u.ListingId == item.ListingId);

                if (existing is null)
                {
                    item.CartId = userCart.CartId;
                    item.Cart = userCart;
                    userCart.Items.Add(item);
                    _unitOfWork.Repository<CartItem>().Update(item);
                }
                else
                {
                    existing.Quantity += item.Quantity;
                    _unitOfWork.Repository<CartItem>().Update(existing);
                    _unitOfWork.Repository<CartItem>().Delete(item);
                }
            }

            // ابتدا جابه‌جایی اقلام ذخیره می‌شود، بعد سبد خالی مهمان حذف می‌شود؛
            // وگرنه cascade ممکن است اقلام منتقل‌شده را هم پاک کند.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _unitOfWork.Repository<Cart>().Delete(anonymousCart);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Anonymous cart {SessionId} merged into cart {CartId}",
                sessionId, userCart.CartId);

            return await MapCartAsync(userCart, cancellationToken);
        }

        public async Task<OrderDto> CheckoutAsync(
            int? userId,
            string? sessionId,
            CheckoutDto dto,
            CancellationToken cancellationToken = default)
        {
            if (!userId.HasValue)
                throw new BusinessException(
                    "برای نهایی‌کردن سفارش باید وارد حساب کاربری شوید.", "CHECKOUT_REQUIRES_LOGIN");

            if (string.IsNullOrWhiteSpace(dto.ShippingFullName)
                || string.IsNullOrWhiteSpace(dto.ShippingAddress)
                || string.IsNullOrWhiteSpace(dto.ShippingPhoneNumber))
                throw new BusinessException("اطلاعات ارسال سفارش کامل نیست.", "CHECKOUT_SHIPPING_REQUIRED");

            var cart = await FindCartAsync(userId, null, tracking: true, cancellationToken);

            if (cart is null || cart.Items.Count == 0)
                throw new BusinessException("سبد خرید خالی است.", "CART_EMPTY");

            // ---- اعتبارسنجی کامل پیش از ساخت سفارش ----
            // هدف: تا جای ممکن از سفارش نیمه‌کاره جلوگیری کنیم.
            var listings = await LoadListingsAsync(cart.Items.Select(i => i.ListingId), cancellationToken);
            var productNames = await LoadProductNamesAsync(listings, cancellationToken);

            foreach (var item in cart.Items.OrderBy(i => i.CartItemId))
            {
                if (!listings.TryGetValue(item.ListingId, out var listing))
                    throw new BusinessException(
                        $"پیشنهاد فروش {item.ListingId} دیگر موجود نیست.", "LISTING_NOT_FOUND");

                if (listing.Status != SellerProductStatus.Active ||
                    listing.Seller == null ||
                    listing.Seller.Status != SellerStatus.Approved ||
                    !listing.Seller.IsActive)
                    throw new BusinessException(
                        $"«{listing.Variant?.Name ?? listing.VariantId.ToString()}» دیگر قابل خرید نیست.", "LISTING_NOT_ACTIVE");

                await EnsureStockAsync(item.ListingId, item.Quantity, cancellationToken);
            }

            var customer = await _unitOfWork.Repository<Customer>()
                .FirstOrDefaultAsync(
                    new Spec<Customer>().Where(c => c.UserId == userId.Value),
                    cancellationToken);

            if (customer is null)
            {
                // M7.11: Fallback auto-provision Customer profile for legacy users
                customer = new Customer { UserId = userId.Value };
                await _unitOfWork.Repository<Customer>().AddAsync(customer, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // ---- محاسبه روش ارسال و تخفیف کوپن (M7.11 & M7.12) ----
            decimal shippingCost = 0;
            if (dto.ShippingMethodId.HasValue)
            {
                var shippingMethod = await _unitOfWork.Repository<ShippingMethod>()
                    .GetByIdAsync(dto.ShippingMethodId.Value, cancellationToken);
                if (shippingMethod is null || !shippingMethod.IsActive)
                    throw new BusinessException("روش ارسال نامعتبر یا غیرفعال است.", "INVALID_SHIPPING_METHOD");

                shippingCost = shippingMethod.BaseCost;
            }

            decimal discountAmount = 0;
            int? couponId = null;
            Coupon? couponEntity = null;
            if (!string.IsNullOrWhiteSpace(dto.CouponCode))
            {
                var normalizedCode = dto.CouponCode.Trim().ToUpperInvariant();
                couponEntity = await _unitOfWork.Repository<Coupon>()
                    .FirstOrDefaultAsync(new Spec<Coupon>().Where(c => c.Code == normalizedCode && c.IsActive).AsTracking(), cancellationToken);

                if (couponEntity is null)
                    throw new BusinessException("کد تخفیف معتبر نیست.", "INVALID_COUPON");

                if (couponEntity.ExpiresAtUtc.HasValue && couponEntity.ExpiresAtUtc.Value < DateTime.UtcNow)
                    throw new BusinessException("کد تخفیف منقضی شده است.", "COUPON_EXPIRED");

                // بررسی سقف استفاده کلی کوپن (M7.12)
                if (couponEntity.UsageLimit.HasValue && couponEntity.UsedCount >= couponEntity.UsageLimit.Value)
                    throw new BusinessException("ظرفیت استفاده از این کد تخفیف به پایان رسیده است.", "COUPON_LIMIT_REACHED");

                // بررسی سقف استفاده به ازای هر کاربر (M7.12)
                if (couponEntity.UsageLimitPerUser.HasValue)
                {
                    var userUsageCount = await _unitOfWork.Repository<Order>()
                        .CountAsync(new Spec<Order>().Where(o => o.CustomerId == customer.CustomerId && o.CouponId == couponEntity.CouponId), cancellationToken);

                    if (userUsageCount >= couponEntity.UsageLimitPerUser.Value)
                        throw new BusinessException("شما به سقف مجاز استفاده از این کد تخفیف رسیده‌اید.", "COUPON_LIMIT_REACHED");
                }

                var itemsTotal = cart.Items.Sum(i =>
                {
                    listings.TryGetValue(i.ListingId, out var l);
                    return (l?.Price ?? i.UnitPrice) * i.Quantity;
                });

                if (couponEntity.MinOrderAmount.HasValue && itemsTotal < couponEntity.MinOrderAmount.Value)
                    throw new BusinessException($"حداقل مبلغ سفارش برای اعمال این کد تخفیف {couponEntity.MinOrderAmount.Value:N0} است.", "COUPON_MIN_AMOUNT_NOT_MET");

                discountAmount = Math.Round(itemsTotal * (couponEntity.DiscountPercentage / 100m), 2);
                if (couponEntity.MaxDiscountAmount.HasValue && discountAmount > couponEntity.MaxDiscountAmount.Value)
                    discountAmount = couponEntity.MaxDiscountAmount.Value;

                couponId = couponEntity.CouponId;
            }

            // ---- ساخت سفارش در یک تراکنش (R9) ----
            // بدون تراکنش، شکست در وسط کار یک سفارش نیمه‌کاره با موجودی رزروشده
            // باقی می‌گذاشت: پول و کالا از هم جدا می‌شدند.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            OrderDto result;
            int orderId;

            try
            {
                var created = await _orderService.CreateEmptyOrderAsync(
                    customer.CustomerId,
                    dto.ShippingFullName,
                    dto.ShippingAddress,
                    dto.ShippingPhoneNumber,
                    cancellationToken);

                orderId = created.OrderId;
                result = created;

                var itemsDto = cart.Items.OrderBy(i => i.CartItemId)
                    .Select(item => new AddOrderItemDto { ListingId = item.ListingId, Quantity = item.Quantity });

                result = await _orderService.AddItemsToOrderAsync(orderId, itemsDto, cancellationToken);

                // اعمال تخفیف و هزینه ارسال به سفارش و تسهیم بین گروه‌های فروشنده (M7.12)
                var orderEntity = await _unitOfWork.Repository<Order>()
                    .FirstOrDefaultAsync(new Spec<Order>().Where(o => o.OrderId == orderId).Include(o => o.SellerGroups).AsTracking(), cancellationToken);

                if (orderEntity != null)
                {
                    orderEntity.ApplyDiscountsAndShipping(discountAmount, shippingCost, couponId, dto.ShippingMethodId);

                    var totalItemsSubTotal = orderEntity.SellerGroups.Sum(g => g.SubTotal);
                    var groupCount = orderEntity.SellerGroups.Count;

                    decimal allocatedDiscount = 0;
                    decimal allocatedShipping = 0;
                    var groupsList = orderEntity.SellerGroups.ToList();

                    for (int i = 0; i < groupsList.Count; i++)
                    {
                        var group = groupsList[i];
                        decimal groupDiscount;
                        decimal groupShipping;

                        if (i == groupsList.Count - 1)
                        {
                            groupDiscount = Math.Max(0, discountAmount - allocatedDiscount);
                            groupShipping = Math.Max(0, shippingCost - allocatedShipping);
                        }
                        else
                        {
                            decimal ratio = totalItemsSubTotal > 0 ? group.SubTotal / totalItemsSubTotal : (1m / groupCount);
                            groupDiscount = Math.Round(discountAmount * ratio, 2, MidpointRounding.AwayFromZero);
                            groupShipping = Math.Round(shippingCost / groupCount, 2, MidpointRounding.AwayFromZero);

                            allocatedDiscount += groupDiscount;
                            allocatedShipping += groupShipping;
                        }

                        group.SetDiscountAndShippingShare(groupDiscount, groupShipping);
                        _unitOfWork.Repository<OrderSellerGroup>().Update(group);
                    }

                    _unitOfWork.Repository<Order>().Update(orderEntity);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    result = _mapper != null ? _mapper.Map<OrderDto>(orderEntity) : new OrderDto
                    {
                        OrderId = orderEntity.OrderId,
                        OrderDate = orderEntity.OrderDate,
                        TotalAmount = orderEntity.TotalAmount,
                        Status = orderEntity.Status.ToString(),
                        ShippingFullName = orderEntity.ShippingFullName,
                        ShippingAddress = orderEntity.ShippingAddress,
                        ShippingPhoneNumber = orderEntity.ShippingPhoneNumber,
                        ShippingCost = orderEntity.ShippingCost,
                        DiscountAmount = orderEntity.DiscountAmount
                    };
                }

                // افزایش شمارنده استفاده از کوپن (M7.12)
                if (couponEntity != null)
                {
                    couponEntity.UsedCount++;
                    _unitOfWork.Repository<Coupon>().Update(couponEntity);
                }

                // ---- پاک‌سازی سبد ----
                foreach (var item in cart.Items.ToList())
                {
                    _unitOfWork.Repository<CartItem>().Delete(item);
                    cart.Items.Remove(item);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                // با توکن لغوشده rollback هم لغو می‌شد و تراکنش باز می‌ماند.
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                throw;
            }

            _logger.LogInformation(
                "Cart {CartId} checked out into order {OrderId}",
                cart.CartId, orderId);

            return result;
        }
    }
}