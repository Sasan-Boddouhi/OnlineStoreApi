using Application.Entities;

namespace BusinessLogic.DTOs.Listing
{
    public class CreateSellerProductDto
    {
        public int SellerId { get; set; }
        /// <summary>Phase 6: پیشنهاد فروش روی یک واریانت مشخص ساخته می‌شود.</summary>
        public int VariantId { get; set; }
        public decimal Price { get; set; }
        public SellerProductStatus Status { get; set; } = SellerProductStatus.Draft;
    }
}