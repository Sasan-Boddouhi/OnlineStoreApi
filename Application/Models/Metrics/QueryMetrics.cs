using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Models.Metrics
{
    public class QueryMetrics
    {
        public string Path { get; set; } = default!;
        public string? Filter { get; set; }
        public string? Sort { get; set; }
        public int FilterLength { get; set; }
        public int SortFields { get; set; }
        public int FilterConditions { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public bool HasException { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
    }
}
