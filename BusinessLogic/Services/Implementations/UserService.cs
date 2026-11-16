using Application.Common.Helpers;
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
    private readonly TimeProvider _timeProvider;

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
        IAuditService auditService,
        TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _logger = logger;
        _cacheService = cacheService;
        _mapper = mapper;
        _auditService = auditService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // Batch 48b: فقط شکل استاندارد 09… ذخیره شده است؛ ورودی غیرقابل نرمال‌سازی کاربری ندارد.
        var phone = IranianPhoneNumber.NormalizeOrNull(phoneNumber);
        if (phone is null)
            return null;

        var spec = new Spec<User>()
            .Where(u => u.PhoneNumber == phone)
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

        EnsureNotSystemAccount(user);

        // Batch 48b: مقایسه، بررسی تکرار و ذخیره فقط با شکل استاندارد 09….
        var phone = IranianPhoneNumber.NormalizeOrThrow(dto.PhoneNumber);

        // Batch 11b: ادمین معمولی با عوض کردن موبایل یا ایمیل یک مدیر ارشد (و سپس بازیابی رمز)
        // حساب او را تصاحب می‌کرد. حساب مدیر ارشد را فقط مدیر ارشد ویرایش می‌کند → 403 ADMIN_CANNOT_MODIFY_SUPERADMIN.
        await EnsureSuperAdminTargetAllowedAsync(
            user,
            _currentUserService.GetCurrentUserId(),
            losesSuperAdmin: false,
            cancellationToken);

        if (user.PhoneNumber != phone)
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.PhoneNumber == phone && x.UserId != dto.UserId, cancellationToken);
            if (exists)
                throw new ConflictException("شماره موبایل قبلاً ثبت شده است.", "USER_PHONE_EXISTS");
            user.SecurityStamp = Guid.NewGuid().ToString();
            // Batch 46: شمارهٔ تازه را مالکش با OTP تأیید نکرده است.
            user.PhoneNumberConfirmed = false;
        }

        // Batch 11 (F-41): ایمیل در دیتابیس یکتاست؛ تکرار آن پیش‌تر به خطای ذخیره‌سازی می‌رسید.
        if (!string.IsNullOrWhiteSpace(dto.Email)
            && await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.Email == dto.Email && x.UserId != dto.UserId, cancellationToken))
        {
            throw new ConflictException("این ایمیل قبلاً ثبت شده است.", "USER_EMAIL_EXISTS");
        }

        _mapper.Map(dto, user);
        user.PhoneNumber = phone;

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

    public async Task<UserDto?> UpdateProfileAsync(int userId, UpdateUserProfileDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating profile for user ID: {UserId}", userId);

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User not found for profile update with ID: {UserId}", userId);
            return null;
        }

        user.FirstName = dto.FirstName.Trim();
        user.LastName = dto.LastName.Trim();
        if (!string.IsNullOrWhiteSpace(dto.DateOfBirth))
            user.DateOfBirth = PersianDateHelper.ToGregorian(dto.DateOfBirth);

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await InvalidateUserCache(userId, cancellationToken);
        _logger.LogInformation("User profile updated successfully: {UserId}", userId);

        return await GetByIdAsync(userId, includeRoles: true, cancellationToken);
    }

    #endregion

    #region DeleteAsync

    /// <summary>
    /// Batch 11 (F-22): «حذف» کاربر حذف نرم است: غیرفعال‌سازی + باطل کردن همهٔ نشست‌ها.
    /// حذف واقعی با سفارش‌ها و سوابق مالی کاربر (کلید خارجی Restrict) به 500 می‌رسید و تاریخچه را از بین می‌برد.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Soft-deleting (deactivating) user with ID: {UserId}", id);
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User not found for deletion with ID: {UserId}", id);
            return false;
        }

        EnsureNotSystemAccount(user);

        await EnsureCanChangeAccountAsync(user, removesAccess: true, cancellationToken);
        await DeactivateAndRevokeSessionsAsync(user, cancellationToken);

        _logger.LogInformation("User {UserId} deactivated by delete request", id);
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

        EnsureNotSystemAccount(user);

        await EnsureCanChangeAccountAsync(user, removesAccess: !isActive, cancellationToken);

        if (isActive)
        {
            user.IsActive = true;
            user.SecurityStamp = Guid.NewGuid().ToString();
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await DeactivateAndRevokeSessionsAsync(user, cancellationToken);
        }

        _logger.LogInformation("User {UserId} active status updated to: {IsActive}", id, isActive);
        await InvalidateUserCache(id, cancellationToken);
        return true;
    }

    /// <summary>
    /// Batch 11 (F-22): غیرفعال‌سازی، SecurityStamp را عوض می‌کند (access tokenهای موجود در درخواست بعدی رد می‌شوند)
    /// و همهٔ نشست‌ها و refresh tokenهای کاربر را در همان SaveChanges باطل می‌کند.
    /// </summary>
    private async Task DeactivateAndRevokeSessionsAsync(User user, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        user.IsActive = false;
        user.SecurityStamp = Guid.NewGuid().ToString();
        _unitOfWork.Repository<User>().Update(user);

        var sessions = await _unitOfWork.Repository<UserSession>().ListAsync(
            new Spec<UserSession>()
                .Where(s => s.UserId == user.UserId && s.Status == UserSession.SessionStatus.Active)
                .AsTracking(),
            cancellationToken);
        foreach (var session in sessions)
        {
            session.Status = UserSession.SessionStatus.Revoked;
            session.RevokedAtUtc = now;
        }

        var tokens = await _unitOfWork.Repository<RefreshTokenEntity>().ListAsync(
            new Spec<RefreshTokenEntity>()
                .Where(t => t.UserId == user.UserId && !t.IsRevoked)
                .AsTracking(),
            cancellationToken);
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAtUtc = now;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "User {UserId} deactivated; {SessionCount} sessions and {TokenCount} refresh tokens revoked",
            user.UserId, sessions.Count, tokens.Count);
    }

    /// <summary>
    /// Batch 11 (F-22): گاردهای تغییر حساب کاربری توسط ادمین. ترتیب بررسی:
    ///  ۱. غیرفعال‌سازی یا حذف حساب خود → 409 CANNOT_DEACTIVATE_SELF
    ///  ۲. از دست رفتن آخرین مدیر ارشد فعال → 409 LAST_SUPERADMIN
    ///  ۳. تغییر حساب مدیر ارشد توسط کسی که خودش مدیر ارشد نیست → 403 ADMIN_CANNOT_MODIFY_SUPERADMIN
    /// </summary>
    private async Task EnsureCanChangeAccountAsync(User target, bool removesAccess, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUserId();

        if (removesAccess && currentUserId == target.UserId)
        {
            _logger.LogWarning("User {UserId} attempted to deactivate or delete their own account", currentUserId);
            throw new ConflictException(
                "نمی‌توانید حساب کاربری خودتان را غیرفعال یا حذف کنید.",
                "CANNOT_DEACTIVATE_SELF");
        }

        await EnsureSuperAdminTargetAllowedAsync(target, currentUserId, losesSuperAdmin: removesAccess, cancellationToken);
    }

    private async Task EnsureSuperAdminTargetAllowedAsync(
        User target,
        int currentUserId,
        bool losesSuperAdmin,
        CancellationToken cancellationToken)
    {
        var superAdminRole = await _unitOfWork.Repository<Role>().FirstOrDefaultAsync(
            new Spec<Role>().Where(r => r.Code == SuperAdminRoleCode), cancellationToken);
        if (superAdminRole is null)
            return;

        var superAdminRoleId = superAdminRole.RoleId;
        var userRoles = _unitOfWork.Repository<UserRole>();

        var targetIsSuperAdmin = await userRoles.AnyAsync(
            ur => ur.UserId == target.UserId && ur.RoleId == superAdminRoleId, cancellationToken);
        if (!targetIsSuperAdmin)
            return;

        if (losesSuperAdmin && target.IsActive)
        {
            var anotherActiveSuperAdmin = await userRoles.AnyAsync(
                ur => ur.RoleId == superAdminRoleId && ur.UserId != target.UserId && ur.User.IsActive,
                cancellationToken);
            if (!anotherActiveSuperAdmin)
            {
                _logger.LogWarning(
                    "User {CurrentUserId} attempted to remove the last active SuperAdmin {TargetUserId}",
                    currentUserId, target.UserId);
                throw new ConflictException(
                    "آخرین مدیر ارشد فعال را نمی‌توان غیرفعال، حذف یا از نقش مدیر ارشد خارج کرد.",
                    "LAST_SUPERADMIN");
            }
        }

        var currentUserIsSuperAdmin = await userRoles.AnyAsync(
            ur => ur.UserId == currentUserId && ur.RoleId == superAdminRoleId, cancellationToken);
        if (!currentUserIsSuperAdmin)
        {
            _logger.LogWarning(
                "User {CurrentUserId} (non-SuperAdmin) attempted to modify SuperAdmin {TargetUserId}",
                currentUserId, target.UserId);
            throw new ForbiddenException(
                "فقط مدیر ارشد می‌تواند حساب یا نقش یک مدیر ارشد را تغییر دهد.",
                "ADMIN_CANNOT_MODIFY_SUPERADMIN");
        }
    }

    #endregion

    /// <summary>
    /// Batch 44 (پاسخ مالک به Batch 49b): حساب سیستمی (مثلاً مالک نشانی انبار پلتفرم) از هیچ API فعال، ویرایش، حذف یا
    /// نقش‌دار نمی‌شود → 409 USER_IS_SYSTEM. فقط سرویس‌های راه‌اندازی آن را می‌سازند و نگه می‌دارند.
    /// </summary>
    private void EnsureNotSystemAccount(User user)
    {
        if (!user.IsSystem)
            return;

        _logger.LogWarning(
            "User {CurrentUserId} attempted to change system account {TargetUserId}",
            _currentUserService.GetCurrentUserId(), user.UserId);
        throw new ConflictException(
            "این حساب سیستمی است و قابل فعال‌سازی، ویرایش، حذف یا نقش‌دهی نیست.",
            ErrorCodes.UserIsSystem.Code);
    }

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
            throw new ConflictException(
                "USER_MUST_HAVE_AT_LEAST_ONE_ROLE",
                "USER_MUST_HAVE_AT_LEAST_ONE_ROLE");
        }

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new NotFoundException("USER_NOT_FOUND", "USER_NOT_FOUND");

        EnsureNotSystemAccount(user);

        // --- Privilege escalation guard (Q1 fix) ---
        // Resolve the SuperAdmin role record once so we can check its RoleId.
        var superAdminRole = await _unitOfWork.Repository<Role>().FirstOrDefaultAsync(
            new Spec<Role>().Where(r => r.Code == SuperAdminRoleCode), cancellationToken);
        if (superAdminRole is null)
        {
            _logger.LogError("SuperAdmin role not found in the database; cannot evaluate privilege escalation guard");
            throw new NotFoundException("SUPERADMIN_ROLE_NOT_FOUND", "SUPERADMIN_ROLE_NOT_FOUND");
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
            throw new ConflictException("CANNOT_REVOKE_OWN_SUPERADMIN", "CANNOT_REVOKE_OWN_SUPERADMIN");
        }

        // Batch 11 (F-22): نقش‌های یک مدیر ارشد را فقط مدیر ارشد تغییر می‌دهد و آخرین مدیر ارشد فعال تنزل نمی‌یابد.
        await EnsureSuperAdminTargetAllowedAsync(
            user,
            currentUserId,
            losesSuperAdmin: !roleIds.Contains(superAdminRole.RoleId),
            cancellationToken);

        // --- Transaction wrapper (Q2 fix) ---
        // Q5 trade-off: User has no RowVersion column, so we rely on explicit transaction
        // boundaries for atomicity rather than EF optimistic concurrency tokens.
        // Concurrent writers follow a "last write wins" policy — the transaction prevents
        // partial data loss but does not serialize concurrent callers.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Capture old role codes for the audit trail (Q3 fix).
            var existing = await _unitOfWork.Repository<UserRole>().ListAsync(
                new Spec<UserRole>().Where(ur => ur.UserId == userId), cancellationToken);

            var oldRoleIds = existing.Select(e => e.RoleId).Distinct().ToList();
            var oldRoleEntities = oldRoleIds.Count > 0
                ? await _unitOfWork.Repository<Role>().ListAsync(new Spec<Role>().Where(r => oldRoleIds.Contains(r.RoleId)), cancellationToken)
                : (IReadOnlyList<Role>)Array.Empty<Role>();

            var oldRoleCodes = oldRoleEntities.Select(r => r.Code).ToList();
            foreach (var existingEntry in existing)
            {
                _unitOfWork.Repository<UserRole>().Delete(existingEntry);
            }

            var distinctRoleIds = roleIds.Distinct().ToList();
            var newRoleEntities = distinctRoleIds.Count > 0
                ? await _unitOfWork.Repository<Role>().ListAsync(new Spec<Role>().Where(r => distinctRoleIds.Contains(r.RoleId)), cancellationToken)
                : (IReadOnlyList<Role>)Array.Empty<Role>();

            if (newRoleEntities.Count != distinctRoleIds.Count)
                throw new NotFoundException("ROLE_NOT_FOUND", "ROLE_NOT_FOUND");

            var newRoleCodes = new List<string>();
            foreach (var role in newRoleEntities)
            {
                var userRole = new UserRole { UserId = userId, RoleId = role.RoleId };
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