using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.ProductSubcategory
{
    public class ProductSubcategoryDto
    {
        public int SubcategoryId { get; set; }
        public required string SubcategoryName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }
    }
}
