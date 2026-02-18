using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Queries
{
    public class FilterRequest
    {
        public string Field { get; set; } = null!;

        public string Operator { get; set; } = "eq";

        public object? Value { get; set; }
    }
}
