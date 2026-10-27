using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Role;
using BusinessLogic.DTOs.Permission;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class RoleService : IRoleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<RoleService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating role: {RoleCode}", dto.Code);

        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new BusinessException("کد نقش الزامی است.", "ROLE_CODE_REQUIRED");

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            throw new BusinessException("نام نمایشی نقش الزامی است.", "ROLE_DISPLAY_NAME_REQUIRED");

        var codeExists = await _unitOfWork.Repository<Role>()
            .AnyAsync(r => r.Code == dto.Code, cancellationToken);
        if (codeExists)
            throw new BusinessException("نقشی با این کد قبلاً وجود دارد.", "ROLE_CODE_DUPLICATE");

        var entity = _mapper.Map<Role>(dto);
        entity.IsSystem = false; // Custom roles are never system roles

        await _unitOfWork.Repository<Role>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Add permissions in batch
        var distinctPermIds = dto.PermissionIds.Distinct().ToList();
        if (distinctPermIds.Count > 0)
        {
            var validPerms = await _unitOfWork.Repository<Permission>()
                .ListAsync(new Spec<Permission>().Where(p => distinctPermIds.Contains(p.PermissionId)), cancellationToken);

            foreach (var perm in validPerms)
            {
                await _unitOfWork.Repository<RolePermission>().AddAsync(
                    new RolePermission { RoleId = entity.RoleId, PermissionId = perm.PermissionId },
                    cancellationToken);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Role created with ID: {RoleId}", entity.RoleId);

        return await GetByIdAsync(entity.RoleId, cancellationToken) ?? _mapper.Map<RoleDto>(entity);
    }

    private async Task<RoleDto> BuildRoleDtoAsync(Role entity, CancellationToken cancellationToken)
    {
        // Load permissions separately since Spec doesn't support ThenInclude
        var rolePerms = await _unitOfWork.Repository<RolePermission>()
            .ListAsync(new Spec<RolePermission>().Where(rp => rp.RoleId == entity.RoleId), cancellationToken);

        var permissionIds = rolePerms.Select(rp => rp.PermissionId).Distinct().ToList();
        var permissions = permissionIds.Count > 0
            ? await _unitOfWork.Repository<Permission>()
                .ListAsync(new Spec<Permission>().Where(p => permissionIds.Contains(p.PermissionId)), cancellationToken)
            : new List<Permission>();

        var dto = _mapper.Map<RoleDto>(entity);
        dto.Permissions = permissions.Select(p => p.Code).ToList();
        return dto;
    }

    public async Task<RoleDto?> GetByIdAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Role>().GetByIdAsync(roleId, cancellationToken);
        if (entity == null)
            return null;

        return await BuildRoleDtoAsync(entity, cancellationToken);
    }

    public async Task<RoleDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Role>()
            .FirstOrDefaultAsync(new Spec<Role>().Where(r => r.Code == code), cancellationToken);
        if (entity == null)
            return null;

        return await BuildRoleDtoAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _unitOfWork.Repository<Role>()
            .ListAsync(new Spec<Role>(), cancellationToken);

        var result = new List<RoleDto>(entities.Count);
        foreach (var entity in entities)
        {
            result.Add(await BuildRoleDtoAsync(entity, cancellationToken));
        }

        return result;
    }

    public async Task<RoleDto?> UpdateAsync(int roleId, UpdateRoleDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating role {RoleId}", roleId);

        var entity = await _unitOfWork.Repository<Role>().GetByIdAsync(roleId, cancellationToken);
        if (entity == null)
            return null;

        // Prevent updating system roles
        if (entity.IsSystem)
            throw new BusinessException("نقش‌های سیستمی قابل ویرایش نیستند.", "ROLE_SYSTEM_CANNOT_UPDATE");

        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new BusinessException("کد نقش الزامی است.", "ROLE_CODE_REQUIRED");

        var codeExists = await _unitOfWork.Repository<Role>()
            .AnyAsync(r => r.Code == dto.Code && r.RoleId != roleId, cancellationToken);
        if (codeExists)
            throw new BusinessException("نقشی با این کد قبلاً وجود دارد.", "ROLE_CODE_DUPLICATE");

        // Update properties
        entity.Code = dto.Code;
        entity.DisplayName = dto.DisplayName;
        entity.IsActive = dto.IsActive;
        entity.Description = dto.Description;

        // Update permissions
        var existingPerms = await _unitOfWork.Repository<RolePermission>()
            .ListAsync(new Spec<RolePermission>().Where(rp => rp.RoleId == roleId), cancellationToken);
        
        foreach (var ep in existingPerms)
        {
            _unitOfWork.Repository<RolePermission>().Delete(ep);
        }

        var distinctPermIds = dto.PermissionIds.Distinct().ToList();
        if (distinctPermIds.Count > 0)
        {
            var validPerms = await _unitOfWork.Repository<Permission>()
                .ListAsync(new Spec<Permission>().Where(p => distinctPermIds.Contains(p.PermissionId)), cancellationToken);

            foreach (var perm in validPerms)
            {
                await _unitOfWork.Repository<RolePermission>().AddAsync(
                    new RolePermission { RoleId = roleId, PermissionId = perm.PermissionId },
                    cancellationToken);
            }
        }

        _unitOfWork.Repository<Role>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Role {RoleId} updated", roleId);

        return await GetByIdAsync(roleId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting role {RoleId}", roleId);

        var entity = await _unitOfWork.Repository<Role>().GetByIdAsync(roleId, cancellationToken);
        if (entity == null)
            return false;

        // Prevent deleting system roles
        if (entity.IsSystem)
            throw new BusinessException("نقش‌های سیستمی قابل حذف نیستند.", "ROLE_SYSTEM_CANNOT_DELETE");

        // Check if role is assigned to any user
        var hasUsers = await _unitOfWork.Repository<UserRole>()
            .AnyAsync(ur => ur.RoleId == roleId, cancellationToken);

        if (hasUsers)
            throw new BusinessException("این نقش به کاربران تخصیص داده شده و قابل حذف نیست.", "ROLE_HAS_USERS");

        _unitOfWork.Repository<Role>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Role {RoleId} deleted", roleId);

        return true;
    }
}