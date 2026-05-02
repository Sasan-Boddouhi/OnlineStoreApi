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
        Task<PagedResult<EmployeeTypeDto>> GetAllAsync(
            string? filter,
            string? sort,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);
    }
}