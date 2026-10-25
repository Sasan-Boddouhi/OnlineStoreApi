using BusinessLogic.DTOs.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Log
{
    public class LogFilterDto
    {
        public string? Search { get; set; }
        public string? Level { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? SortOrder { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
