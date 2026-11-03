using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessLogic.DTOs.Brand;

namespace BusinessLogic.Services.Interfaces;

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BrandDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<BrandDto> CreateAsync(CreateBrandDto dto, CancellationToken cancellationToken = default);
    Task<BrandDto?> UpdateAsync(int id, UpdateBrandDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
