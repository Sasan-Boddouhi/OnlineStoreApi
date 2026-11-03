using System.Collections.Generic;
using BusinessLogic.DTOs.ProductSubcategory;

namespace BusinessLogic.DTOs.ProductCategory
{
    public class ProductCategoryDto
    {
        public int ProductCategoryId { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        public List<ProductSubcategoryDto> Subcategories { get; set; } = new();
    }
}