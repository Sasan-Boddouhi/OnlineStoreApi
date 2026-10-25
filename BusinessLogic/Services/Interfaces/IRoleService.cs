using BusinessLogic.DTOs.Role;
using BusinessLogic.DTOs.Permission;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IRoleService
    {
        Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken cancellationToken = default);
        Task<RoleDto?> GetByIdAsync(int roleId, CancellationToken cancellationToken = default);
        Task<RoleDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<RoleDto?> UpdateAsync(int roleId, UpdateRoleDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int roleId, CancellationToken cancellationToken = default);
    }

    public interface IPermissionService
    {
        Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<PermissionDto?> GetByIdAsync(int permissionId, CancellationToken cancellationToken = default);
        Task<PermissionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    }
}