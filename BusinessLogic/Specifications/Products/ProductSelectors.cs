using Application.Entities;
using BusinessLogic.DTOs.Product;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Products
{
    public static class ProductSelectors
    {
        public static Expression<Func<Product, ProductDto>> ToDto =>
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
                IsActive = p.IsActive,
                Barcode = p.Barcode,
                Weight = p.Weight,
                Dimensions = p.Dimensions
            };
    }
}
