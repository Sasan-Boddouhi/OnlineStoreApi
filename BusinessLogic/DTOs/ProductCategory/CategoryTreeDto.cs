using System.Collections.Generic;
using BusinessLogic.DTOs.ProductSubcategory;

namespace BusinessLogic.DTOs.ProductCategory;

public class CategoryTreeDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public List<ProductSubcategoryDto> Subcategories { get; set; } = new();
}
