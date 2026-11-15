using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces;

public interface IProductService
{
    /// <summary>
    /// فهرست محصولات. Batch 6 (F-15): به‌طور پیش‌فرض فقط محصولات فعال؛
    /// <paramref name="includeInactive"/> را فقط کنترلر و فقط برای دارندهٔ مجوز catalog.products.write روشن می‌کند.
    /// </summary>
    Task<PagedResult<ProductDto>> GetByQueryAsync(
        QueryContract<Product> query,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// همان فهرست با شرط‌های نوع‌دار (Batch 60، API-10). شرط‌های <paramref name="criteria"/> با AND به فیلتر
    /// <paramref name="query"/> اضافه می‌شوند؛ مرتب‌سازی آماده (اگر باشد) جای مرتب‌سازی query را می‌گیرد.
    /// </summary>
    Task<PagedResult<ProductDto>> GetByQueryAsync(
        QueryContract<Product> query,
        ProductListCriteria criteria,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default);
    Task<ProductDto?> UpdateAsync(UpdateProductDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    // ---------------- Phase 6: Variants & Attributes ----------------

    Task<ProductVariantDto> CreateVariantAsync(
        CreateProductVariantDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductVariantDto>> GetVariantsAsync(
        int productId,
        CancellationToken cancellationToken = default);

    Task<VariantAttributeDto> CreateAttributeAsync(
        CreateProductAttributeDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VariantAttributeDto>> GetAttributesAsync(
        int productId,
        CancellationToken cancellationToken = default);

    // M7.1: Variant CRUD completion
    Task<ProductVariantDto?> UpdateVariantAsync(int variantId, UpdateProductVariantDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteVariantAsync(int variantId, CancellationToken cancellationToken = default);
}