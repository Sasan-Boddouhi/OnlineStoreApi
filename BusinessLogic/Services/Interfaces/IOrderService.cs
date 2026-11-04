using Application.Entities;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces
{
    public interface IOrderService
    {
        // ایجاد سفارش خالی (بدون آیتم)
        Task<OrderDto> CreateEmptyOrderAsync(int customerId, string shippingFullName, string shippingAddress, string shippingPhone, string? idempotencyKey = null, CancellationToken cancellationToken = default);

        // افزودن آیتم به سفارش (اگر سفارش در وضعیت Pending باشد)
        Task<OrderDto> AddItemToOrderAsync(int orderId, AddOrderItemDto itemDto, CancellationToken cancellationToken = default);

        // افزودن دسته‌ای آیتم‌ها به سفارش در یک مرحله
        Task<OrderDto> AddItemsToOrderAsync(int orderId, IEnumerable<AddOrderItemDto> itemsDto, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // حذف آیتم از سفارش
        Task<OrderDto> RemoveItemFromOrderAsync(int orderId, int orderItemId, CancellationToken cancellationToken = default);

        // تأیید سفارش (تغییر وضعیت به Processing)
        Task<OrderDto> ConfirmOrderAsync(int orderId, CancellationToken cancellationToken = default);

        // دریافت تمام سفارش‌های کاربر جاری
        Task<IEnumerable<OrderDto>> GetOrdersAsync(int userId, CancellationToken cancellationToken = default);

        // دریافت جزئیات یک سفارش متعلق به کاربر جاری (یا ادمین)
        Task<OrderDetailsDto?> GetOrderDetailsAsync(int userId, int orderId, CancellationToken cancellationToken = default);

        // دریافت فهرست سفارش‌ها برای پنل ادمین با صفحه‌بندی و فیلتر وضعیت
        Task<PagedResult<OrderDto>> GetAdminOrdersAsync(OrderStatus? status, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // لغو سفارش (اگر امکان‌پذیر باشد)
        Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

        // Phase 3: تحویل سفارش و ساخت تسویهٔ فروشندگان (عملیات مدیریتی)
        Task<OrderDto> MarkDeliveredAsync(int orderId, CancellationToken cancellationToken = default);

        // Phase 9: مرجوعی سفارش تحویل‌شده
        Task<OrderDto> MarkReturnedAsync(int orderId, CancellationToken cancellationToken = default);
    }
}