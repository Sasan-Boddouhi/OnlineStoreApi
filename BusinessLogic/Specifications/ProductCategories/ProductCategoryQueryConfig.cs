using Application.Entities;
using BusinessLogic.DTOs.ProductCategory;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.ProductCategories;

public static class ProductCategoryQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "categoryname",
        "isactive"
    };

    // پروجکشن ساده (بدون زیردسته‌ها)
    public static Expression<Func<ProductCategory, ProductCategoryDto>> Projection =>
        pc => new ProductCategoryDto
        {
            ProductCategoryId = pc.CategoryId,
            Name = pc.CategoryName,
            IsActive = pc.IsActive
        };
}