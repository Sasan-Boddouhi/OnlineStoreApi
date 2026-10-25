using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetByQueryAsync(
        QueryContract<Product> query,
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
}