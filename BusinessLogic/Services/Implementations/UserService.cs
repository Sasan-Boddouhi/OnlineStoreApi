using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Application.Exceptions;
using Application.Interfaces.Security;
using System.Linq;
using AutoMapper;

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

    public async Task<UserDto> CreateAsync(CreateUserDto dto)
    {
        _logger.LogInformation("Creating new user with phone: {PhoneNumber}", dto.PhoneNumber);
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            // اعتبارسنجی تکراری نبودن شماره موبایل
            var exists = await _unitOfWork
                .Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber);

            if (exists)
            {
                _logger.LogWarning("Duplicate phone number detected: {PhoneNumber}", dto.PhoneNumber);
                throw new BusinessException("شماره موبایل قبلاً ثبت شده است.");
            }

            var user = _mapper.Map<User>(dto);
            user.PasswordHash = _passwordHasher.Hash(dto.Password);
            user.SecurityStamp = Guid.NewGuid().ToString();
            user.IsActive = true;

            await _unitOfWork.Repository<User>().AddAsync(user);

            if (dto.Addresses?.Any() == true)
            {
                foreach (var addressDto in dto.Addresses)
                {
                    var address = _mapper.Map<Address>(addressDto);
                    address.UserId = user.UserId;
                    await _unitOfWork.Repository<Address>().AddAsync(address);
                }
            }

            // یک بار ذخیره و commit کل تراکنش
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            _logger.LogInformation("User created successfully with ID: {UserId}", user.UserId);

            _cache.Remove($"{ALL_USERS_FULL_CACHE_KEY}_all");

            return MapToSimpleUserDto(user);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Failed to create user with phone: {PhoneNumber}", dto.PhoneNumber);
            throw;
        }
    }

    #endregion

    #region Get By Id (simple)

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        _logger.LogDebug("Retrieving user by ID: {UserId}", id);

        try
        {
            var spec = new UserByIdProjectionSpecification(id);

            var userDto = await _unitOfWork.Repository<User>().FirstOrDefaultAsync(spec);
            if (userDto is null)
                _logger.LogDebug("User not found with ID: {UserId}", id);

            return userDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with ID: {UserId}", id);
            throw;
        }
    }

    #endregion

    #region Get By Phone Number (simple)

    public async Task<UserDto?> GetByPhoneNumberAsync(string phoneNumber)
    {
        _logger.LogDebug("Retrieving user by phone: {PhoneNumber}", phoneNumber);

        try
        {
            var spec = new UserByPhoneProjectionSpecification(phoneNumber);

            var userDto = await _unitOfWork.Repository<User>().FirstOrDefaultAsync(spec);
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

    #region Get All (simple, with optional search)

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(string? search = null)
    {
        _logger.LogInformation("Retrieving all users with search: {Search}", search ?? "<none>");

        try
        {
            var spec = new UserProjectionSpecification(search, null, true, null, null);
            var users = await _unitOfWork
                .Repository<User>()
                .ListAsync(spec);

            _logger.LogDebug("Retrieved {Count} users", users.Count);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all users");
            throw;
        }
    }

    #endregion

    #region Get Paged (simple)

    public async Task<PagedResult<UserDto>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search,
        string? sortBy,
        bool ascending)
    {
        _logger.LogInformation("Retrieving paged users - Page: {PageNumber}, Size: {PageSize}, Search: {Search}, SortBy: {SortBy}, Ascending: {Ascending}",
            pageNumber, pageSize, search ?? "<none>", sortBy ?? "UserId", ascending);

        try
        {
            var skip = (pageNumber - 1) * pageSize;

            var itemSpec = new UserProjectionSpecification(search, sortBy, ascending, skip, pageSize);
            var countSpec = new UserCountSpecification(search);

            var items = await _unitOfWork
                .Repository<User>()
                .ListAsync(itemSpec);

            var totalCount = await _unitOfWork
                .Repository<User>()
                .CountAsync(countSpec);

            _logger.LogDebug("Paged result - TotalCount: {TotalCount}, Returned: {Returned}", totalCount, items.Count);

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

    #region Get All With Role (full data, cached)

    public async Task<IReadOnlyList<UserDto>> GetAllUsersWithRoleNameAsync(string? search = null)
    {
        _logger.LogInformation("Retrieving all users with role information, search: {Search}", search ?? "<none>");

        var cacheKey = $"{ALL_USERS_FULL_CACHE_KEY}_{search ?? "all"}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<UserDto>? cachedList) && cachedList is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
            return cachedList;
        }

        try
        {
            var spec = new UserWithRoleProjectionSpecification(search, null, true, 1, int.MaxValue);
            var users = await _unitOfWork
                .Repository<User>()
                .ListAsync(spec);

            _logger.LogDebug("Retrieved {Count} users with roles from database", users.Count);

            _cache.Set(cacheKey, users, _cacheEntryOptions);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users with role information");
            throw;
        }
    }

    #endregion

    #region Get By Id With Role (full data, cached)

    public async Task<UserDto?> GetByIdWithRoleNameAsync(int id)
    {
        _logger.LogInformation("Retrieving user with role information, Id: {UserId}", id);

        var cacheKey = $"{USER_FULL_CACHE_KEY_PREFIX}{id}";

        if (_cache.TryGetValue(cacheKey, out UserDto? cachedUser) && cachedUser is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
            return cachedUser;
        }

        try
        {
            var spec = new UserWithRoleByIdProjectionSpecification(id);

            var userDto = await _unitOfWork.Repository<User>().FirstOrDefaultAsync(spec);
            if (userDto is null)
            {
                _logger.LogDebug("User not found with ID: {UserId}", id);
                return null;
            }

            _logger.LogDebug("User retrieved with role from database: {UserId}", id);
            _cache.Set(cacheKey, userDto, _cacheEntryOptions);
            return userDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with role information, Id: {UserId}", id);
            throw;
        }
    }

    #endregion

    #region Update

    public async Task<UserDto?> UpdateAsync(UpdateUserDto dto)
    {
        _logger.LogInformation("Updating user with ID: {UserId}", dto.UserId);

        try
        {
            var user = await _unitOfWork
                .Repository<User>()
                .GetByIdAsync(dto.UserId);

            if (user is null)
            {
                _logger.LogWarning("User not found for update with ID: {UserId}", dto.UserId);
                return null;
            }

            if (user.PhoneNumber != dto.PhoneNumber)
            {
                var exists = await _unitOfWork
                    .Repository<User>()
                    .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber && x.UserId != dto.UserId);

                if (exists)
                {
                    _logger.LogWarning("Duplicate phone number during update: {PhoneNumber}", dto.PhoneNumber);
                    throw new BusinessException("شماره موبایل قبلاً ثبت شده است.");
                }

                user.SecurityStamp = Guid.NewGuid().ToString();
            }

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.PhoneNumber = dto.PhoneNumber;

            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User updated successfully: {UserId}", dto.UserId);

            // Invalidate cache
            _cache.Remove($"{USER_FULL_CACHE_KEY_PREFIX}{user.UserId}");
            _cache.Remove(ALL_USERS_FULL_CACHE_KEY);

            return await GetByIdWithRoleNameAsync(user.UserId);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            _logger.LogError(ex, "Error updating user with ID: {UserId}", dto.UserId);
            throw;
        }
    }

    #endregion

    #region Delete

    public async Task<bool> DeleteAsync(int id)
    {
        _logger.LogInformation("Deleting user with ID: {UserId}", id);

        try
        {
            var user = await _unitOfWork
                .Repository<User>()
                .GetByIdAsync(id);

            if (user is null)
            {
                _logger.LogWarning("User not found for deletion with ID: {UserId}", id);
                return false;
            }

            _unitOfWork.Repository<User>().Delete(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User deleted successfully: {UserId}", id);

            // Invalidate cache
            _cache.Remove($"{USER_FULL_CACHE_KEY_PREFIX}{id}");
            _cache.Remove(ALL_USERS_FULL_CACHE_KEY);

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

    public async Task<bool> SetActiveStatusAsync(int id, bool isActive)
    {
        _logger.LogInformation("Setting active status for user {UserId} to: {IsActive}", id, isActive);

        try
        {
            var user = await _unitOfWork
                .Repository<User>()
                .GetByIdAsync(id);

            if (user is null)
            {
                _logger.LogWarning("User not found for status update with ID: {UserId}", id);
                return false;
            }

            user.IsActive = isActive;
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User {UserId} active status updated to: {IsActive}", id, isActive);

            // Invalidate cache
            _cache.Remove($"{USER_FULL_CACHE_KEY_PREFIX}{id}");
            _cache.Remove(ALL_USERS_FULL_CACHE_KEY);

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

    public async Task<UserDto?> GetCurrentUserAsync()
    {
        var currentUserId = _currentUserService.GetCurrentUserId();

        if (currentUserId == 0)
        {
            _logger.LogWarning("Current user ID is 0 - user may not be authenticated");
            return null;
        }

        _logger.LogDebug("Retrieving current user with ID: {CurrentUserId}", currentUserId);
        return await GetByIdWithRoleNameAsync(currentUserId);
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

    public Task<PagedResult<UserDto>> GetPagedWithRolesAsync(int pageNumber, int pageSize, string? search = null, string? sortBy = "UserId", bool ascending = true)
    {
        throw new NotImplementedException();
    }

    Task<IEnumerable<UserDto>> IUserService.GetAllAsync(string? search)
    {
        throw new NotImplementedException();
    }

    Task<IEnumerable<UserDto>> IUserService.GetAllUsersWithRoleNameAsync(string? search)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<string>> GetAllRolesAsync()
    {
        throw new NotImplementedException();
    }

    public Task<List<string>> GetRolesByUserTypeAsync(string? userType)
    {
        throw new NotImplementedException();
    }

    #endregion
}