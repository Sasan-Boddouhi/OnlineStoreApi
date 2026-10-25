using BusinessLogic.DTOs.Listing;

namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// عملیات موجودی (Phase 0).
/// موجودی فقط از طریق این قرارداد تغییر می‌کند تا یک منبع واحد حقیقت باقی بماند.
/// </summary>
public interface IInventoryService
{
    /// <summary>موجودی قابل فروش یک پیشنهاد فروش (Listing) در همه انبارها.</summary>
    Task<decimal> GetAvailableAsync(int listingId, CancellationToken cancellationToken = default);

    /// <summary>رزرو (کسر) موجودی. در صورت کمبود، BusinessException پرتاب می‌شود.</summary>
    Task ReserveAsync(int listingId, decimal quantity, CancellationToken cancellationToken = default);

    /// <summary>آزادسازی (برگرداندن) موجودی.</summary>
    Task ReleaseAsync(int listingId, decimal quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 10 (R49) — افزودن موجودی به یک پیشنهاد فروش.
    ///
    /// پیش از این هیچ راهی برای نوشتن موجودی وجود نداشت: نه endpoint، نه سرویس.
    /// یعنی هر Listing تازه با موجودی صفر متولد می‌شد و checkout همیشه
    /// INSUFFICIENT_STOCK می‌داد. این متد آن بن‌بست را باز می‌کند.
    ///
    /// مجوز: مالک Listing اگر فروشنده است؛ یا نقش مدیریتی برای هر Listing.
    /// </summary>
    Task<InventoryStockDto> AddStockAsync(
        int listingId,
        int? warehouseId,
        decimal quantity,
        CancellationToken cancellationToken = default);
}