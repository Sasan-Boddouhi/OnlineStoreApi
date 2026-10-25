using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Helper;
using Application.Interfaces;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using AutoMapper;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Users;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BusinessLogic.Services.Implementations;

public sealed class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UserService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IMapper _mapper;
    private readonly IAuditService _auditService;

    private const string USER_FULL_CACHE_KEY_PREFIX = "UserFull_";
    private const string ALL_USERS_FULL_CACHE_KEY = "AllUsersFull";
    private const string SuperAdminRoleCode = "SuperAdmin";

    public UserService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService,
        ILogger<UserService> logger,
        ICacheService cacheService,
        IMapper mapper,
        IAuditService auditService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _logger = logger;
        _cacheService = cacheService;
        _mapper = mapper;
        _auditService = auditService;
    }

    #region GetByQueryAsync (متد اصلی جستجو)

    public async Task<PagedResult<UserDto>> GetByQueryAsync(
        QueryContract<User> query,
        CancellationToken cancellationToken = default)
    {
        var spec = query.ToSpec();

        var items = await _unitOfWork
            .Repository<User>()
            .ListAsync(
                spec,
                UserQueryConfig.Projection,
                cancellationToken);

        foreach (var item in items)
        {
            if (item.DateOfBirth.HasValue)
            {
                item.DateOfBirthPersian =
                    PersianDateHelper.ToPersian(
                        item.DateOfBirth.Value);
            }
        }

        var totalCount = await _unitOfWork
            .Repository<User>()
            .CountAsync(
                spec,
                cancellationToken);

        int pageNumber;
        int pageSize;

        if (query.Skip.HasValue || query.Take.HasValue)
        {
            pageSize = query.Take ?? 20;

            var skip = query.Skip ?? 0;

            pageNumber = (skip / pageSize) + 1;
        }
        else
        {
            pageNumber = query.Page ?? 1;

            pageSize = query.Size ?? 20;
        }

        return new PagedResult<UserDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    #endregion

    #region GetByIdAsync (با پشتیبانی کش)

    public async Task<UserDto?> GetByIdAsync(
        int id,
        bool includeRoles = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving user by ID: {UserId}, includeRoles: {IncludeRoles}", id, includeRoles);

        if (includeRoles)
        {
            var cacheKey = $"{USER_FULL_CACHE_KEY_PREFIX}{id}";
            var cachedUser = await _cacheService.GetAsync<UserDto>(cacheKey, cancellationToken);
            if (cachedUser is not null)
            {
                _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                return cachedUser;
            }
        }

        var spec = new Spec<User>()
            .Where(u => u.UserId == id)
            .Where(u => u.IsActive);

        var userDto = await _unitOfWork.Repository<User>()
            .FirstOrDefaultAsync(spec, UserQueryConfig.Projection, cancellationToken);

        if (userDto is null)
        {
            _logger.LogDebug("User not found with ID: {UserId}", id);
            return null;
        }

        if (includeRoles)
        {
            var cacheKey = $"{USER_FULL_CACHE_KEY_PREFIX}{id}";
            await _cacheService.SetAsync(
                cacheKey,
                userDto,
                TimeSpan.FromMinutes(30),
                cancellationToken);
            _logger.LogDebug("User cached with role for ID: {UserId}", id);
        }

        return userDto;
    }

    #endregion

    #region GetByPhoneNumberAsync

    public async Task<UserDto?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving user by phone: {PhoneNumber}", phoneNumber);

        var spec = new Spec<User>()
            .Where(u => u.PhoneNumber == phoneNumber)
            .Where(u => u.IsActive);

        return await _unitOfWork.Repository<User>()
            .FirstOrDefaultAsync(spec, UserQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region GetCurrentUserAsync

    public async Task<UserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUserService.GetCurrentUserId();
        if (currentUserId == 0)
        {
            _logger.LogWarning("Current user ID is 0 - user may not be authenticated");
            return null;
        }

        _logger.LogDebug("Retrieving current user with ID: {CurrentUserId}", currentUserId);
        return await GetByIdAsync(currentUserId, includeRoles: true, cancellationToken);
    }

    #endregion

    #region UpdateAsync

    public async Task<UserDto?> UpdateAsync(UpdateUserDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating user with ID: {UserId}", dto.UserId);

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(dto.UserId, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User not found for update with ID: {UserId}", dto.UserId);
            return null;
        }

        if (user.PhoneNumber != dto.PhoneNumber)
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber && x.UserId != dto.UserId, cancellationToken);
            if (exists)
                throw new BusinessException("شماره موبایل قبلاً ثبت شده است.", "USER_PHONE_EXISTS");
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        _mapper.Map(dto, user);

        if (!string.IsNullOrWhiteSpace(dto.DateOfBirth))
        {
            user.DateOfBirth = PersianDateHelper.ToGregorian(dto.DateOfBirth);
        }

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User updated successfully: {UserId}", dto.UserId);
        await InvalidateUserCache(user.UserId, cancellationToken);

        return await GetByIdAsync(user.UserId, includeRoles: true, cancellationToken);
    }

    #endregion

    #region DeleteAsync

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting user with ID: {UserId}", id);
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User not found for deletion with ID: {UserId}", id);
            return false;
        }

        _unitOfWork.Repository<User>().Delete(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User deleted successfully: {UserId}", id);
        await InvalidateUserCache(id, cancellationToken);
        return true;
    }

    #endregion

    #region SetActiveStatusAsync

    public async Task<bool> SetActiveStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting active status for user {UserId} to: {IsActive}", id, isActive);
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User not found for status update with ID: {UserId}", id);
            return false;
        }

        user.IsActive = isActive;
        user.SecurityStamp = Guid.NewGuid().ToString();
        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} active status updated to: {IsActive}", id, isActive);
        await InvalidateUserCache(id, cancellationToken);
        return true;
    }

    #endregion

    #region GetRolesAsync

    public async Task<IEnumerable<string>> GetRolesAsync(string? userType = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving roles by user type: {UserType}", userType ?? "All");
        var spec = new Spec<User>();
        if (!string.IsNullOrWhiteSpace(userType) && Enum.TryParse<UserType>(userType, out var userTypeEnum))
            spec.Where(u => u.UserType == userTypeEnum);

        spec.Include(u => u.Employee!.EmployeeType);

        var users = await _unitOfWork.Repository<User>().ListAsync(spec, cancellationToken);
        var roles = users
            .Select(u => u.Employee?.EmployeeType?.TypeName ?? (u.UserType == UserType.Customer ? "Customer" : "NoRole"))
            .Distinct()
            .ToList();
        return roles;
    }

    #endregion

    #region AssignRolesAsync (R45)

    public async Task AssignRolesAsync(int userId, List<int> roleIds, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Assigning roles {RoleIds} to user {UserId}", string.Join(",", roleIds), userId);

        // Q4 fix: reject empty role lists — a user must always have at least one role.
        if (roleIds is null || !roleIds.Any())
        {
            _logger.LogWarning("User {UserId} attempted to assign empty role list to user {TargetUserId}",
                _currentUserService.GetCurrentUserId(), userId);
            throw new BusinessException(
                "USER_MUST_HAVE_AT_LEAST_ONE_ROLE",
                "USER_MUST_HAVE_AT_LEAST_ONE_ROLE");
        }

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new NotFoundException("USER_NOT_FOUND", "USER_NOT_FOUND");

        // --- Privilege escalation guard (Q1 fix) ---
        // Resolve the SuperAdmin role record once so we can check its RoleId.
        var superAdminRole = await _unitOfWork.Repository<Role>().FirstOrDefaultAsync(
            new Spec<Role>().Where(r => r.Code == SuperAdminRoleCode), cancellationToken);
        if (superAdminRole is null)
        {
            _logger.LogError("SuperAdmin role not found in the database; cannot evaluate privilege escalation guard");
            throw new BusinessException("SUPERADMIN_ROLE_NOT_FOUND", "SUPERADMIN_ROLE_NOT_FOUND");
        }

        var currentUserId = _currentUserService.GetCurrentUserId();
        var currentUserRoles = await _unitOfWork.Repository<UserRole>().ListAsync(
            new Spec<UserRole>().Where(ur => ur.UserId == currentUserId), cancellationToken);

        var currentUserRoleIds = currentUserRoles.Select(ur => ur.RoleId).ToHashSet();
        var isCurrentUserSuperAdmin = currentUserRoleIds.Contains(superAdminRole.RoleId);

        // Q1 fix: Only a SuperAdmin may assign the SuperAdmin role to anyone.
        // An Admin (not SuperAdmin) attempting to assign SuperAdmin — even to themselves —
        // must be blocked.
        if (!isCurrentUserSuperAdmin && roleIds.Contains(superAdminRole.RoleId))
        {
            _logger.LogWarning(
                "Privilege escalation attempt: user {CurrentUserId} (non-SuperAdmin) attempted to assign SuperAdmin role to user {TargetUserId}",
                currentUserId, userId);
            throw new ForbiddenException(
                "ADMIN_CANNOT_ASSIGN_SUPERADMIN",
                "ADMIN_CANNOT_ASSIGN_SUPERADMIN");
        }

        // Original self-removal guard: a SuperAdmin must not strip their own SuperAdmin role.
        if (currentUserId == userId && isCurrentUserSuperAdmin && !roleIds.Contains(superAdminRole.RoleId))
        {
            _logger.LogWarning("User {UserId} attempted to remove their own SuperAdmin role", userId);
            throw new BusinessException("CANNOT_REVOKE_OWN_SUPERADMIN", "CANNOT_REVOKE_OWN_SUPERADMIN");
        }

        // --- Transaction wrapper (Q2 fix) ---
        // Q5 trade-off: User has no RowVersion column, so we rely on explicit transaction
        // boundaries for atomicity rather than EF optimistic concurrency tokens.
        // Concurrent writers follow a "last write wins" policy — the transaction prevents
        // partial data loss but does not serialize concurrent callers.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Capture old role codes for the audit trail (Q3 fix).
            var oldRoleCodes = new List<string>();
            var existing = await _unitOfWork.Repository<UserRole>().ListAsync(
                new Spec<UserRole>().Where(ur => ur.UserId == userId), cancellationToken);
            foreach (var existingEntry in existing)
            {
                var oldRole = await _unitOfWork.Repository<Role>().GetByIdAsync(
                    existingEntry.RoleId, cancellationToken);
                if (oldRole is not null)
                    oldRoleCodes.Add(oldRole.Code);
                _unitOfWork.Repository<UserRole>().Delete(existingEntry);
            }

            var newRoleCodes = new List<string>();
            foreach (var roleId in roleIds.Distinct())
            {
                var role = await _unitOfWork.Repository<Role>().GetByIdAsync(roleId, cancellationToken);
                if (role is null)
                    throw new BusinessException("ROLE_NOT_FOUND", "ROLE_NOT_FOUND");

                var userRole = new UserRole { UserId = userId, RoleId = roleId };
                await _unitOfWork.Repository<UserRole>().AddAsync(userRole, cancellationToken);
                newRoleCodes.Add(role.Code);
            }

            // Q3 fix: audit object includes BOTH old and new role codes.
            var auditData = new { OldRoles = oldRoleCodes, NewRoles = newRoleCodes };
            var detailsJson = JsonSerializer.Serialize(auditData);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            await _auditService.RecordAsync(
                "User.RolesAssigned",
                "User",
                userId.ToString(),
                detailsJson,
                cancellationToken);

            user.SecurityStamp = Guid.NewGuid().ToString();
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await InvalidateUserCache(userId, cancellationToken);
            _logger.LogInformation("Roles assigned successfully to user {UserId}", userId);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private async Task InvalidateUserCache(int userId, CancellationToken cancellationToken)
    {
        await _cacheService.RemoveAsync($"{USER_FULL_CACHE_KEY_PREFIX}{userId}", cancellationToken);
        await _cacheService.RemoveAsync(ALL_USERS_FULL_CACHE_KEY, cancellationToken);
    }

    #endregion
}