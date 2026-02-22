using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Product
{
    public class UpdateProductDto
    {
        public int ProductId { get; set; }
        public string? Name { get; internal set; }
    }
}
