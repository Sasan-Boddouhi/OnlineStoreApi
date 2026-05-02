using BusinessLogic.DTOs.Employee;
using BusinessLogic.DTOs.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IEmployeeService
    {
        Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default);
        Task<EmployeeDto?> UpdateAsync(UpdateEmployeeDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<EmployeeDto?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);
        Task<PagedResult<EmployeeDto>> GetAllAsync(
            string? filter,
            string? sort,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);
    }
}
