using Application.DTOs.Order;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;

namespace BusinessLogic.Services.Interfaces
{
    public interface IOrderService
    {
        // ایجاد سفارش خالی (بدون آیتم)
        Task<OrderDto> CreateEmptyOrderAsync(int customerId, string shippingFullName, string shippingAddress, string shippingPhone, CancellationToken cancellationToken = default);

        // افزودن آیتم به سفارش (اگر سفارش در وضعیت Pending باشد)
        Task<OrderDto> AddItemToOrderAsync(int orderId, AddOrderItemDto itemDto, CancellationToken cancellationToken = default);

        // حذف آیتم از سفارش
        Task<OrderDto> RemoveItemFromOrderAsync(int orderId, int orderItemId, CancellationToken cancellationToken = default);

        // تأیید سفارش (تغییر وضعیت به Processing)
        Task<OrderDto> ConfirmOrderAsync(int orderId, CancellationToken cancellationToken = default);

        // دریافت تمام سفارش‌های یک مشتری
        Task<IEnumerable<OrderDto>> GetOrdersAsync(int customerId, CancellationToken cancellationToken = default);

        // دریافت جزئیات یک سفارش (به همراه آیتم‌ها و فاکتور)
        Task<OrderDetailsDto?> GetOrderDetailsAsync(int orderId, CancellationToken cancellationToken = default);

        // لغو سفارش (اگر امکان‌پذیر باشد)
        Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    }
}