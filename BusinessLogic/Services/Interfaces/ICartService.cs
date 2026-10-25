using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Order;

namespace BusinessLogic.Services.Interfaces
{
    /// <summary>
    /// Phase 4 — سبد خرید و نهایی‌سازی سفارش.
    ///
    /// ⚠️ مالک سبد همیشه از هویت درخواست (کاربر یا نشست ناشناس) تعیین می‌شود،
    /// نه از پارامتر cartId. اگر cartId از بیرون می‌آمد، هر کاربر می‌توانست
    /// سبد دیگری را بخواند یا تغییر دهد (IDOR).
    /// </summary>
    public interface ICartService
    {
        /// <summary>سبد فعلی را برمی‌گرداند و در صورت نبود، می‌سازد.</summary>
        Task<CartDto> GetCartAsync(int? userId, string? sessionId, CancellationToken cancellationToken = default);

        /// <summary>افزودن قلم. فقط Listing فعال و موجود پذیرفته می‌شود.</summary>
        Task<CartDto> AddItemAsync(int? userId, string? sessionId, AddCartItemDto dto, CancellationToken cancellationToken = default);

        /// <summary>تغییر تعداد یک قلم. تعداد صفر یا کمتر، قلم را حذف می‌کند.</summary>
        Task<CartDto> UpdateQuantityAsync(int? userId, string? sessionId, int listingId, decimal quantity, CancellationToken cancellationToken = default);

        /// <summary>حذف یک قلم.</summary>
        Task<CartDto> RemoveItemAsync(int? userId, string? sessionId, int listingId, CancellationToken cancellationToken = default);

        /// <summary>خالی‌کردن سبد (قلم‌ها حذف می‌شوند، خود سبد می‌ماند).</summary>
        Task ClearCartAsync(int? userId, string? sessionId, CancellationToken cancellationToken = default);

        /// <summary>ادغام سبد مهمان در سبد کاربر هنگام ورود.</summary>
        Task<CartDto> MergeAnonymousCartAsync(string sessionId, int userId, CancellationToken cancellationToken = default);

        /// <summary>تبدیل سبد به سفارش: رزرو موجودی، ساخت گروه‌های فروشنده، پاک‌سازی سبد.</summary>
        Task<OrderDto> CheckoutAsync(int? userId, string? sessionId, CheckoutDto dto, CancellationToken cancellationToken = default);
    }
}