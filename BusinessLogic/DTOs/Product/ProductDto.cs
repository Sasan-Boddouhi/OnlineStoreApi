using System;
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
    }
}
