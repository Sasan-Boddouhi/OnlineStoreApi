using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Products
{
    public sealed class ProductQueryCountSpecification
        : BaseSpecification<Product>
    {
        public ProductQueryCountSpecification(string? filter)
            : base(ProductQueryConfig.AllowedFields)
        {
            AddCriteria(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(filter))
                ApplyFilterString(filter);
        }
    }
}
