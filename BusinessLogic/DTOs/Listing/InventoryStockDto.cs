using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Listing
{
    /// <summary>
    /// Phase 10 (R49) — پاسخ افزودن موجودی.
    /// «قابل فروش» را هم برمی‌گردانیم چون فروشنده بعد از ثبت موجودی
    /// دقیقاً همین عدد را می‌خواهد بداند، نه شناسه‌های داخلی.
    /// </summary>
    public class InventoryStockDto
    {
        public int ListingId { get; set; }
        public int WarehouseId { get; set; }
        public decimal AddedQuantity { get; set; }
        public decimal AvailableQuantity { get; set; }
    }

    /// <summary>بدنهٔ افزودن موجودی.</summary>
    public class AddStockDto
    {
        /// <summary>مقدار افزوده؛ باید بزرگ‌تر از صفر باشد.</summary>
        [Range(0.001, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// انبار مقصد. اگر خالی بماند، انبار پیش‌فرض پلتفرم استفاده می‌شود.
        /// اجباری نکردنش عمدی است: فروشندهٔ تازه هیچ انباری ندارد و
        /// وادار کردنش به ساختن انبار، دقیقاً همان بن‌بست R49 را بازمی‌سازد.
        /// </summary>
        public int? WarehouseId { get; set; }
    }
}