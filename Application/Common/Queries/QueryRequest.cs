using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Queries
{
    public class QueryRequest
    {
        public string? Search { get; set; }

        public List<FilterRequest>? Filters { get; set; }

        public string? SortBy { get; set; }

        public bool Ascending { get; set; } = true;

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }

}
