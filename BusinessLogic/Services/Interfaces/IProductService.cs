using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<PagedResult<ProductDto>> GetByQueryAsync(string? filter, string? sort, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<ProductDto?> UpdateAsync(UpdateProductDto dto, CancellationToken cancellationToken = default);
    }
}