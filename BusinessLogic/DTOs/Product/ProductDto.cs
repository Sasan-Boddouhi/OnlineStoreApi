using System;
using Application.Entities;
using BusinessLogic.DTOs.Media;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Product
{
    public class ProductDto
    {
        public required int ProductId { get; set; }
        public required string Name { get; set; }
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public required int SubcategoryId { get; set; }
        public bool IsActive { get; set; } = true;
        public required string SubcategoryName { get; set; }
        public required int CategoryId { get; set; }
        public required string CategoryName { get; set; }
        public string? Barcode { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public UnitOfMeasurement Unit { get; set; }

        /// <summary>Phase 5: گالری تصاویر، مرتب‌شده بر اساس SortOrder.</summary>
        public List<ProductImageDto> Images { get; set; } = new();

        /// <summary>Phase 6: واریانت‌های محصول همراه با مقادیر ویژگی‌هایشان.</summary>
        public List<ProductVariantDto> Variants { get; set; } = new();

        /// <summary>Phase 7: میانگین امتیاز نظرات تأییدشده (۰ اگر نظری نباشد).</summary>
        public double AverageRating { get; set; }

        /// <summary>Phase 7: تعداد نظرات تأییدشده.</summary>
        public int ReviewCount { get; set; }
    }
}
