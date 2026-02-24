using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.ProductCategories
{
    public sealed class ProductCategoryWithSubcategoriesSpecification : BaseSpecification<ProductCategory>
    {
        public ProductCategoryWithSubcategoriesSpecification()
        {
            AddInclude(pc => pc.Subcategories);
            ApplyOrderBy(pc => pc.CategoryName);
        }
    }
}
