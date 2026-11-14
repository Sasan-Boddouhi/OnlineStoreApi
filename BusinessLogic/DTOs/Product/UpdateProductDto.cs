using System;
using Application.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Product
{
    public class UpdateProductDto
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int SubcategoryId { get; set; }
        public string Description { get; set; } = string.Empty;

        public string? ImageUrl { get; set; }
        public string? Barcode { get; set; }
        public UnitOfMeasurement? Unit { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public bool? IsActive { get; set; }
        /// <summary>
        /// برند تازهٔ محصول. null یعنی «بدون تغییر» (مانند Unit/IsActive)، نه «حذف برند».
        /// </summary>
        public int? BrandId { get; set; }

        /// <summary>
        /// Batch 49b: true برند محصول را پاک می‌کند (BrandId = null). همراه با <see cref="BrandId"/> مجاز نیست (422).
        /// چون null در <see cref="BrandId"/> «بدون تغییر» است، پاک کردن فقط با این پرچم صریح ممکن است.
        /// </summary>
        public bool ClearBrand { get; set; }
    }
}
