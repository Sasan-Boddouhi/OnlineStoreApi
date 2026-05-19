using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.ProductSubcategory
{
    public class CreateProductSubcategoryDto
    {
        public string? SubcategoryName { get; internal set; }
        public int CategoryId { get; internal set; }
    }
}
