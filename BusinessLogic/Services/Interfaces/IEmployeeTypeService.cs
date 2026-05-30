using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.DTOs.Shared;

namespace BusinessLogic.Services.Interfaces
{
    public interface IEmployeeTypeService
    {
        Task<EmployeeTypeDto> CreateAsync(CreateEmployeeTypeDto dto, CancellationToken cancellationToken = default);
        Task<EmployeeTypeDto?> UpdateAsync(UpdateEmployeeTypeDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<EmployeeTypeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<PagedResult<EmployeeTypeDto>> GetByQueryAsync(QueryContract<EmployeeType> query, CancellationToken cancellationToken = default);
    }
}