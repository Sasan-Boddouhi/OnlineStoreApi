using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.ProductCategory
{
    public class ProductCategoryDto
    {
        public int ProductCategoryId { get; internal set; }
        public string Name { get; internal set; }
        public bool IsActive { get; internal set; }
    }
}
