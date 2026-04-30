using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.Specifications.Products;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.ProductSubcategories
{
    public sealed class ProductSubcategoryQueryCountSpecification : BaseSpecification<ProductSubcategory>
    {
        public ProductSubcategoryQueryCountSpecification(int? categoryId)
        {
            Criteria = ps => ps.CategoryId == categoryId && ps.IsActive;
            //AddCriteria(ps => ps.CategoryId == categoryId && ps.IsActive);
        }
    }
}