using BusinessLogic.DTOs.ProductCategory;

namespace BusinessLogic.Services.Interfaces;

/// <summary>
/// قرارداد دسته‌بندی محصول (Phase 0).
/// پیش از این، این interface خالی بود و متدهای ProductCategoryService از هیچ مسیری قابل فراخوانی نبودند.
/// </summary>
public interface IProductCategoryService
{
    Task<ProductCategoryDto> CreateAsync(
        CreateProductCategoryDto dto,
        CancellationToken cancellationToken = default);

    Task<ProductCategoryDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ProductCategoryDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ProductCategoryDto?> UpdateAsync(
        UpdateProductCategoryDto dto,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}