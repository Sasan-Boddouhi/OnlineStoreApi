using Application.Entities;
using BusinessLogic.DTOs.Product;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Products;

public static class ProductQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "name",
        "price",
        "description",
        "subcategory.subcategoryname",
        "isactive",
        "ExpirationDate"
    };

    // پروجکشن کامل (با دسته‌بندی و زیردسته‌بندی)
    public static Expression<Func<Product, ProductDto>> Projection =>
        p => new ProductDto
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Price = p.Price,
            Description = p.Description,
            SubcategoryId = p.SubcategoryId,
            CategoryId = p.Subcategory.CategoryId,
            SubcategoryName = p.Subcategory != null ? p.Subcategory.SubcategoryName : null,
            CategoryName = p.Subcategory != null && p.Subcategory.Category != null ? p.Subcategory.Category.CategoryName : null,
            IsActive = p.IsActive,
            Barcode = p.Barcode
        };
}