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

        public List<FilterRequest> Filters { get; set; } = new List<FilterRequest>();

        public string? SortBy { get; set; }

        public bool Ascending { get; set; } = true;

        private int _pageNumber = 1;
        private int _pageSize = 10;

        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 10 : value;
        }

        public List<string> SearchFields { get; set; } = new List<string>();
    }
}
