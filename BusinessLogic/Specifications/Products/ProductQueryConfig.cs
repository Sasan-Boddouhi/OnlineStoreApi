using Application.Entities;
using BusinessLogic.DTOs.Media;
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
            Barcode = p.Barcode,
            Unit = p.Unit,
            ImageUrl = p.ImageUrl,
            ExpirationDate = p.ExpirationDate,
            Images = p.Images
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.ProductImageId)
                .Select(i => new ProductImageDto
                {
                    ProductImageId = i.ProductImageId,
                    ProductId = i.ProductId,
                    Url = i.Url,
                    Alt = i.Alt,
                    SortOrder = i.SortOrder,
                    IsPrimary = i.IsPrimary
                })
                .ToList(),
            Variants = p.Variants
                .OrderBy(v => v.VariantId)
                .Select(v => new ProductVariantDto
                {
                    VariantId = v.VariantId,
                    ProductId = v.ProductId,
                    Name = v.Name,
                    Sku = v.Sku,
                    Barcode = v.Barcode,
                    IsActive = v.IsActive,
                    Attributes = v.AttributeValues
                        .OrderBy(av => av.AttributeId)
                        .Select(av => new VariantAttributeDto
                        {
                            AttributeId = av.AttributeId,
                            Name = av.Attribute != null ? av.Attribute.Name : string.Empty,
                            Type = av.Attribute != null ? av.Attribute.Type.ToString() : "Text",
                            Value = av.Value
                        })
                        .ToList()
                })
                .ToList(),

            // Phase 9 (R33): از ستون کش‌شده خوانده می‌شود، نه زیرکوئری AVG/COUNT.
            // تازگی این دو عدد مسئولیت ReviewRatingCacheService است.
            AverageRating = p.AverageRating,

            ReviewCount = p.ReviewCount
        };
}