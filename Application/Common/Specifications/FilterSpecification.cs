using Application.Common.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Specifications
{
    public class FilterSpecification<T> : BaseSpecification<T>
    {
        public FilterSpecification(
            string? filter,
            string? sort,
            IReadOnlyList<string>? allowedFields = null)
            : base(allowedFields)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var parser = new FilterParser();
                var criteria = parser.Parse<T>(filter, allowedFields ?? new List<string>());
                AddCriteria(criteria);
            }

            this.ApplySortString(sort);
        }

        public void SetPaging(int pageNumber, int pageSize)
        {
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}
