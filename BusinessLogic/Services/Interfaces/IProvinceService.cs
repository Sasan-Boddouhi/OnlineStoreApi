using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces;

public interface IProvinceService
{
    Task<PagedResult<ProvinceDto>> GetByQueryAsync(
        QueryContract<Province> query,
        CancellationToken cancellationToken = default);

    Task<ProvinceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProvinceDto> CreateAsync(CreateProvinceDto dto, CancellationToken cancellationToken = default);
    Task<ProvinceDto?> UpdateAsync(UpdateProvinceDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}