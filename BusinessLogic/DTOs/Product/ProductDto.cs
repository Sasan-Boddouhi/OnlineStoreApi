using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Product
{
    public class ProductDto
    {
        public int ProductId { get; internal set; }
        public string? Name { get; internal set; }
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public required int SubcategoryId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
