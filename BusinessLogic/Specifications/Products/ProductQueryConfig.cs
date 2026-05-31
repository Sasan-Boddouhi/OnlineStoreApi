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
    // ProductQueryConfig.cs
    public static Expression<Func<Product, ProductDto>> Projection =>
        p => new ProductDto
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Price = p.Price,
            Description = p.Description,
            SubcategoryId = p.SubcategoryId,
            SubcategoryName = p.Subcategory != null ? p.Subcategory.SubcategoryName : string.Empty,
            CategoryId = p.Subcategory != null ? p.Subcategory.CategoryId : 0,
            CategoryName = p.Subcategory != null && p.Subcategory.Category != null
                ? p.Subcategory.Category.CategoryName
                : string.Empty,
            IsActive = p.IsActive,
            Barcode = p.Barcode
        };
}