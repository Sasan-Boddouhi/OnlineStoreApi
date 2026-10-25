using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces;

public interface IProductSubcategoryService
{
    // متد اصلی جستجو (جایگزین GetAllAsync)
    Task<PagedResult<ProductSubcategoryDto>> GetByQueryAsync(
        QueryContract<ProductSubcategory> query,
        CancellationToken cancellationToken = default);

    // متدهای کمکی
    Task<ProductSubcategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> GetCountByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default);

    // عملیات نوشتاری
    Task<ProductSubcategoryDto> CreateAsync(CreateProductSubcategoryDto dto, CancellationToken cancellationToken = default);
    Task<ProductSubcategoryDto> UpdateAsync(UpdateProductSubcategoryDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
}