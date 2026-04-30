using Application.Entities;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.ProductSubcategories
{
    public static class ProductSubcategoryQueryConfig
    {
        public static readonly string[] AllowedFields =
{
            "SubcategoryName",
            "Description",
            "IsActive",
            "Category.CategoryName"
        };

        public static Expression<Func<ProductSubcategory, ProductSubcategoryDto>> Projection =>
            p => new ProductSubcategoryDto
            {
                SubcategoryId = p.SubcategoryId,
                SubcategoryName = p.SubcategoryName,
                Description = p.Description,
                IsActive = p.IsActive,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.CategoryName
            };

        public static Expression<Func<ProductSubcategory, ProductSubcategoryDto>> SimpleProjection =>
            p => new ProductSubcategoryDto
            {

                SubcategoryId = p.SubcategoryId,
                SubcategoryName = p.SubcategoryName,
                Description = p.Description,
                IsActive = p.IsActive
            };
    }
}