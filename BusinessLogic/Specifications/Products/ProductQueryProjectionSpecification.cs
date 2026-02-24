using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Products
{
    public sealed class ProductQueryProjectionSpecification
    : BaseProjectionSpecification<Product, ProductDto>
    {
        public ProductQueryProjectionSpecification(
            string? filter,
            string? sort,
            int? skip,
            int? take)
            : base(ProductQueryConfig.AllowedFields)
        {
            AddCriteria(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(filter))
                ApplyFilterString(filter);

            if (!string.IsNullOrWhiteSpace(sort))
                ApplySortString(sort);

            if (skip.HasValue && take.HasValue)
                ApplyPaging(skip.Value, take.Value);

            SetProjection(p => new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Price = p.Price,
                Description = p.Description,
                SubcategoryId = p.SubcategoryId,
                IsActive = p.IsActive
                // اگر navigation داری:
                // CategoryName = p.Subcategory.Category.Name
            });
        }
    }
}