using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.ProductCategory
{
    public class UpdateProductCategoryDto
    {
        public string Name { get; internal set; }
        public int ProductCategoryId { get; internal set; }
    }
}
