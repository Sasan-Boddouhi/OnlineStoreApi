using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;

namespace BusinessLogic.Services.Interfaces;

public interface IUserService
{
    // متد اصلی جستجو (جایگزین GetAll, GetPaged)
    Task<PagedResult<UserDto>> GetByQueryAsync(
        QueryContract<User> query,
        CancellationToken cancellationToken = default);

    // متدهای کمکی (برای سناریوهای خاص)
    Task<UserDto?> GetByIdAsync(int id, bool includeRoles = false, CancellationToken cancellationToken = default);
    Task<UserDto?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<UserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    // متدهای نوشتاری
    Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateAsync(UpdateUserDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> SetActiveStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default);

    // متد خاص نقش‌ها (در صورت نیاز)
    Task<IEnumerable<string>> GetRolesAsync(string? userType = null, CancellationToken cancellationToken = default);
}