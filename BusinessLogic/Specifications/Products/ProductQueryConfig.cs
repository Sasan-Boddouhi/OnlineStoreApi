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
                // اگر navigation property داری:
                // CategoryName = p.Subcategory.Category.Name,
                IsActive = p.IsActive
            };
    }
}