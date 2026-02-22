using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Product
{
    public class ProductFilterDto
    {
        public string? Search { get; internal set; }
        public int CategoryId { get; internal set; }
        public int SubcategoryId { get; internal set; }
        public decimal MinPrice { get; internal set; }
        public decimal MaxPrice { get; internal set; }
        public int PageNumber { get; internal set; }
        public int PageSize { get; internal set; }
        public string SortOrder { get; internal set; }
    }
}
