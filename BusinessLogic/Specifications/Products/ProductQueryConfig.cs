using Application.Entities;
using BusinessLogic.DTOs.Product;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Products
{
    public static class ProductQueryConfig
    {
        public static readonly string[] AllowedFields =
        {
            "productid",
            "name",
            "price",
            "description",
            "subcategory.name",
            "isactive"
        };

        public static Expression<Func<Product, ProductDto>> Projection =>
            p => new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Price = p.Price,
                Description = p.Description,
                SubcategoryId = p.SubcategoryId,
                SubcategoryName = p.Subcategory.SubcategoryName,
                CategoryId = p.Subcategory.CategoryId,
                CategoryName = p.Subcategory.Category.CategoryName,
                IsActive = p.IsActive
            };
    }
}