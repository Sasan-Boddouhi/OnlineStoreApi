using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
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

        public CartService(
            IUnitOfWork unitOfWork,
            ILogger<CartService> logger,
            IInventoryService inventoryService,
            IOrderService orderService)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _inventoryService = inventoryService;
            _orderService = orderService;
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
        private async Task<Dictionary<int, string>> LoadProductNamesAsync(
            Dictionary<int, SellerProduct> listings,
            CancellationToken cancellationToken)
        {
            var productIds = listings.Values
                .Select(l => l.Variant?.ProductId ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (productIds.Count == 0)
                return new Dictionary<int, string>();

            var spec = new Spec<Product>().Where(p => productIds.Contains(p.ProductId));
            var products = await _unitOfWork.Repository<Product>().ListAsync(spec, cancellationToken);

            return products.ToDictionary(p => p.ProductId, p => p.Name);
        }

        private async Task<SellerProduct> LoadActiveListingAsync(int listingId, CancellationToken cancellationToken)
        {
            var listing = await _unitOfWork.Repository<SellerProduct>()
                .GetByIdAsync(listingId, cancellationToken);

            if (listing is null)
                throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

            if (listing.Status != SellerProductStatus.Active)
                throw new BusinessException("این پیشنهاد فروش در حال حاضر فعال نیست.", "LISTING_NOT_ACTIVE");

            return listing;
        }

        private async Task EnsureStockAsync(int listingId, decimal quantity, CancellationToken cancellationToken)
        {
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
            var productNames = await LoadProductNamesAsync(listings, cancellationToken);

            var items = cart.Items
                .OrderBy(i => i.CartItemId)
                .Select(i =>
                {
                    listings.TryGetValue(i.ListingId, out var listing);
                    var unitPrice = listing?.Price ?? i.UnitPrice;

                    return new CartItemDto
                    {
                        CartItemId = i.CartItemId,
                        ListingId = i.ListingId,
                        SellerId = listing?.SellerId ?? 0,
                        SellerStoreName = listing?.Seller?.StoreName ?? string.Empty,
                        ProductName = productNames.TryGetValue(listing?.Variant?.ProductId ?? 0, out var productName)
                            ? productName
                            : string.Empty,
                        // Phase 7: اگر نام واریانت خالی بود، SKU و در نهایت خالی.
                        VariantName = listing?.Variant?.Name ?? listing?.Variant?.Sku,
                        UnitPrice = unitPrice,
                        Quantity = i.Quantity,
                        LineTotal = unitPrice * i.Quantity
                    };
                })
                .ToList();

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

                if (listing.Status != SellerProductStatus.Active)
                    throw new BusinessException(
                        $"«{listing.Variant?.Name ?? listing.VariantId.ToString()}» دیگر قابل خرید نیست.", "LISTING_NOT_ACTIVE");

                await EnsureStockAsync(item.ListingId, item.Quantity, cancellationToken);
            }

            var customer = await _unitOfWork.Repository<Customer>()
                .FirstOrDefaultAsync(
                    new Spec<Customer>().Where(c => c.UserId == userId.Value),
                    cancellationToken);

            if (customer is null)
                throw new BusinessException("پروفایل مشتری برای این کاربر یافت نشد.", "CUSTOMER_NOT_FOUND");

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

                foreach (var item in cart.Items.OrderBy(i => i.CartItemId))
                {
                    result = await _orderService.AddItemToOrderAsync(
                        orderId,
                        new AddOrderItemDto { ListingId = item.ListingId, Quantity = item.Quantity },
                        cancellationToken);
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