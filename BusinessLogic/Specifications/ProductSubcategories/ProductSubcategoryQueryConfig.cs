using Application.Entities;
using BusinessLogic.DTOs.ProductSubcategory;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.ProductSubcategories;

public static class ProductSubcategoryQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "subcategoryname",
        "category.categoryname",
        "isactive"
    };

    // پروجکشن کامل (با دسته‌بندی)
    public static Expression<Func<ProductSubcategory, ProductSubcategoryDto>> Projection =>
        ps => new ProductSubcategoryDto
        {
            SubcategoryId = ps.SubcategoryId,
            SubcategoryName = ps.SubcategoryName,
            CategoryId = ps.CategoryId,
            CategoryName = ps.Category != null ? ps.Category.CategoryName : null,
            IsActive = ps.IsActive
        };

    // پروجکشن ساده (بدون دسته‌بندی)
    public static Expression<Func<ProductSubcategory, ProductSubcategoryDto>> SimpleProjection =>
        ps => new ProductSubcategoryDto
        {
            SubcategoryId = ps.SubcategoryId,
            SubcategoryName = ps.SubcategoryName,
            CategoryId = ps.CategoryId,
            IsActive = ps.IsActive
        };
}