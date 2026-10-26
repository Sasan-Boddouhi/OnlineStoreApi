using System.ComponentModel.DataAnnotations;
using Application.Entities;

namespace BusinessLogic.DTOs.Listing
{
    public class CreateSellerProductDto
    {
        public int SellerId { get; set; }

        /// <summary>Phase 6: پیشنهاد فروش روی یک واریانت مشخص ساخته می‌شود.</summary>
        [Range(1, int.MaxValue, ErrorMessage = "VariantId must be a positive integer.")]
        public int VariantId { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero.")]
        public decimal Price { get; set; }

        public SellerProductStatus Status { get; set; } = SellerProductStatus.Draft;
    }
}