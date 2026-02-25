using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Application.Exceptions;
using Application.Interfaces.Security;
using AutoMapper;
using Application.Common.Helpers;
using BusinessLogic.Specifications.Users;
using System.Linq.Expressions;

namespace BusinessLogic.Services.Implementations;

public sealed class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UserService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IMapper _mapper;

    private static readonly MemoryCacheEntryOptions _cacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
        Priority = CacheItemPriority.Normal
    };

    private const string USER_FULL_CACHE_KEY_PREFIX = "UserFull_";
    private const string ALL_USERS_FULL_CACHE_KEY = "AllUsersFull";

    public UserService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService,
        ILogger<UserService> logger,
        IMemoryCache cache,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _logger = logger;
        _cache = cache;
        _mapper = mapper;
    }

    #region Create

    public async Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating new user with phone: {PhoneNumber}", dto.PhoneNumber);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var exists = await _unitOfWork
                .Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber, cancellationToken);

            if (exists)
            {
                _logger.LogWarning("Duplicate phone number detected: {PhoneNumber}", dto.PhoneNumber);
                throw new BusinessException("شماره موبایل قبلاً ثبت شده است.");
            }

            var user = _mapper.Map<User>(dto);
            user.PasswordHash = _passwordHasher.Hash(dto.Password);
            user.SecurityStamp = Guid.NewGuid().ToString();
            user.IsActive = true;

            await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (dto.Addresses?.Any() == true)
            {
                var addresses = dto.Addresses
                    .Where(a => !string.IsNullOrWhiteSpace(a.Plaque))
                    .Select(a =>
                    {
                        var address = _mapper.Map<Address>(a);
                        address.UserId = user.UserId;
                        return address;
                    }).ToList();

                await _unitOfWork.Repository<Address>().AddRangeAsync(addresses, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("User created successfully with ID: {UserId}", user.UserId);
            _cache.Remove($"{ALL_USERS_FULL_CACHE_KEY}_all");

            return MapToSimpleUserDto(user);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create user with phone: {PhoneNumber}", dto.PhoneNumber);
            throw;
        }
    }

    #endregion

    #region Get By Id

    public async Task<UserDto?> GetByIdAsync(int id, bool includeRoles = false, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving user by ID: {UserId}, includeRoles: {IncludeRoles}", id, includeRoles);

        if (includeRoles)
        {
            var cacheKey = $"{USER_FULL_CACHE_KEY_PREFIX}{id}";
            if (_cache.TryGetValue(cacheKey, out UserDto? cachedUser) && cachedUser is not null)
            {
                _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                return cachedUser;
            }
        }

        try
        {
            var spec = new QuerySpecification<User, UserDto>(
                filter: $"id eq {id}",
                sort: null,
                skip: null,
                take: null,
                projection: includeRoles ? UserQueryConfig.Projection : UserQueryConfig.SimpleProjection,
                allowedFields: UserQueryConfig.AllowedFields,
                applyDefaultSoftDelete: true
            );

            var userDto = await _unitOfWork
                .Repository<User>()
                .FirstOrDefaultAsync<UserDto>(spec, cancellationToken);

            if (userDto is null)
            {
                _logger.LogDebug("User not found with ID: {UserId}", id);
                return null;
            }

            if (includeRoles)
            {
                var cacheKey = $"{USER_FULL_CACHE_KEY_PREFIX}{id}";
                _cache.Set(cacheKey, userDto, _cacheEntryOptions);
                _logger.LogDebug("User retrieved with role from database, cached for ID: {UserId}", id);
            }

            return userDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with ID: {UserId}", id);
            throw;
        }
    }

    #endregion

    #region Get By Phone Number

    public async Task<UserDto?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving user by phone: {PhoneNumber}", phoneNumber);

        try
        {
            var spec = new QuerySpecification<User, UserDto>(
                filter: $"phonenumber eq '{phoneNumber}'",
                sort: null,
                skip: null,
                take: null,
                projection: UserQueryConfig.SimpleProjection,
                allowedFields: UserQueryConfig.AllowedFields,
                applyDefaultSoftDelete: true
            );

            var userDto = await _unitOfWork
                .Repository<User>()
                .FirstOrDefaultAsync<UserDto>(spec, cancellationToken);

            if (userDto is null)
                _logger.LogDebug("User not found with phone: {PhoneNumber}", phoneNumber);

            return userDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by phone: {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    #endregion

    #region Get All

    public async Task<IEnumerable<UserDto>> GetAllAsync(string? search = null, bool includeRoles = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving all users with search: {Search}, includeRoles: {IncludeRoles}", search ?? "<none>", includeRoles);

        if (includeRoles)
        {
            var cacheKey = $"{ALL_USERS_FULL_CACHE_KEY}_{search ?? "all"}";
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<UserDto>? cachedList) && cachedList is not null)
            {
                _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                return cachedList;
            }
        }

        try
        {
            var filter = string.IsNullOrWhiteSpace(search) ? null : $"firstname contains '{search}' or lastname contains '{search}' or phonenumber contains '{search}'";

            var spec = new QuerySpecification<User, UserDto>(
                filter: filter,
                sort: null,
                skip: null,
                take: null,
                projection: includeRoles ? UserQueryConfig.Projection : UserQueryConfig.SimpleProjection,
                allowedFields: UserQueryConfig.AllowedFields,
                applyDefaultSoftDelete: true
            );

            var users = await _unitOfWork
                .Repository<User>()
                .ListAsync<UserDto>(spec, cancellationToken);

            _logger.LogDebug("Retrieved {Count} users", users.Count);

            if (includeRoles)
            {
                var cacheKey = $"{ALL_USERS_FULL_CACHE_KEY}_{search ?? "all"}";
                _cache.Set(cacheKey, users, _cacheEntryOptions);
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all users");
            throw;
        }
    }

    #endregion

    #region Get Paged

    public async Task<PagedResult<UserDto>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        string? sortBy = "UserId",
        bool ascending = true,
        bool includeRoles = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving paged users - Page: {PageNumber}, Size: {PageSize}, Search: {Search}, SortBy: {SortBy}, Ascending: {Ascending}, IncludeRoles: {IncludeRoles}",
            pageNumber, pageSize, search ?? "<none>", sortBy, ascending, includeRoles);

        try
        {
            QueryGuard.EnsureValid(search, sortBy);

            var filter = string.IsNullOrWhiteSpace(search) ? null : $"firstname contains '{search}' or lastname contains '{search}' or phonenumber contains '{search}'";
            var sort = string.IsNullOrWhiteSpace(sortBy) ? null : (ascending ? sortBy : $"-{sortBy}");

            var skip = (pageNumber - 1) * pageSize;

            var dataSpec = new QuerySpecification<User, UserDto>(
                filter: filter,
                sort: sort,
                skip: skip,
                take: pageSize,
                projection: includeRoles ? UserQueryConfig.Projection : UserQueryConfig.SimpleProjection,
                allowedFields: UserQueryConfig.AllowedFields,
                applyDefaultSoftDelete: true
            );

            var countSpec = new QueryCountSpecification<User>(
                filter: filter,
                allowedFields: UserQueryConfig.AllowedFields,
                applyDefaultSoftDelete: true
            );

            var items = await _unitOfWork
                .Repository<User>()
                .ListAsync<UserDto>(dataSpec, cancellationToken);

            var totalCount = await _unitOfWork
                .Repository<User>()
                .CountAsync(countSpec, cancellationToken);

            return new PagedResult<UserDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paged users");
            throw;
        }
    }

    #endregion

    #region Get Roles

    public async Task<IEnumerable<string>> GetRolesAsync(string? userType = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving roles by user type: {UserType}", userType ?? "All");

        try
        {
            // ساخت شرط فیلتر در صورت وجود userType
            Expression<Func<User, bool>>? criteria = null;
            if (!string.IsNullOrWhiteSpace(userType) && Enum.TryParse<UserType>(userType, out var userTypeEnum))
            {
                criteria = u => u.UserType == userTypeEnum;
            }

            // ایجاد specification با استفاده از ExpressionSpecification (کلاس concrete)
            var spec = criteria == null
                ? new ExpressionSpecification<User>(u => true)  // همه کاربران
                : new ExpressionSpecification<User>(criteria);

            var users = await _unitOfWork.Repository<User>().ListAsync(spec, cancellationToken);

            var roles = users
                .Select(u => u.Employee?.EmployeeType?.TypeName ?? (u.UserType == UserType.Customer ? "Customer" : "NoRole"))
                .Distinct()
                .ToList();

            return roles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving roles");
            throw;
        }
    }

    #endregion

    #region Update

    public async Task<UserDto?> UpdateAsync(UpdateUserDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating user with ID: {UserId}", dto.UserId);

        try
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(dto.UserId, cancellationToken);

            if (user is null)
            {
                _logger.LogWarning("User not found for update with ID: {UserId}", dto.UserId);
                return null;
            }

            if (user.PhoneNumber != dto.PhoneNumber)
            {
                var exists = await _unitOfWork
                    .Repository<User>()
                    .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber && x.UserId != dto.UserId, cancellationToken);

                if (exists)
                {
                    _logger.LogWarning("Duplicate phone number during update: {PhoneNumber}", dto.PhoneNumber);
                    throw new BusinessException("شماره موبایل قبلاً ثبت شده است.");
                }

                user.SecurityStamp = Guid.NewGuid().ToString();
            }

            _mapper.Map(dto, user);

            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User updated successfully: {UserId}", dto.UserId);

            InvalidateUserCache(user.UserId);

            return await GetByIdAsync(user.UserId, includeRoles: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            _logger.LogError(ex, "Error updating user with ID: {UserId}", dto.UserId);
            throw;
        }
    }

    #endregion

    #region Delete

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting user with ID: {UserId}", id);

        try
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);

            if (user is null)
            {
                _logger.LogWarning("User not found for deletion with ID: {UserId}", id);
                return false;
            }

            _unitOfWork.Repository<User>().Delete(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User deleted successfully: {UserId}", id);

            InvalidateUserCache(id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user with ID: {UserId}", id);
            throw;
        }
    }

    #endregion

    #region Set Active Status

    public async Task<bool> SetActiveStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting active status for user {UserId} to: {IsActive}", id, isActive);

        try
        {
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

            InvalidateUserCache(id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating active status for user: {UserId}", id);
            throw;
        }
    }

    #endregion

    #region Current User

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

    #region Helper Methods

    private static UserDto MapToSimpleUserDto(User user) =>
        new()
        {
            UserId = user.UserId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive
        };

    private void InvalidateUserCache(int userId)
    {
        _cache.Remove($"{USER_FULL_CACHE_KEY_PREFIX}{userId}");
        _cache.Remove(ALL_USERS_FULL_CACHE_KEY);
    }

    #endregion
}