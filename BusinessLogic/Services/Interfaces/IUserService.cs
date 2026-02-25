using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;

namespace BusinessLogic.Services.Interfaces
{
    public interface IUserService
    {
        // صفحه‌بندی کاربران با امکان دریافت نقش
        Task<PagedResult<UserDto>> GetPagedAsync(
            int pageNumber,
            int pageSize,
            string? search = null,
            string? sortBy = "UserId",
            bool ascending = true,
            bool includeRoles = false,
            CancellationToken cancellationToken = default);

        // دریافت کاربر با قابلیت انتخاب نقش
        Task<UserDto?> GetByIdAsync(
            int id,
            bool includeRoles = false,
            CancellationToken cancellationToken = default);

        // دریافت همه کاربران با قابلیت انتخاب نقش
        Task<IEnumerable<UserDto>> GetAllAsync(
            string? search = null,
            bool includeRoles = false,
            CancellationToken cancellationToken = default);

        // دریافت نقش‌ها (اختیاری با فیلتر نوع کاربر)
        Task<IEnumerable<string>> GetRolesAsync(
            string? userType = null,
            CancellationToken cancellationToken = default);

        // عملیات اصلی
        Task<UserDto> CreateAsync(
            CreateUserDto dto,
            CancellationToken cancellationToken = default);

        Task<UserDto?> UpdateAsync(
            UpdateUserDto dto,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default);

        // متدهای پرکاربرد اضافی
        Task<UserDto?> GetByPhoneNumberAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default);

        Task<bool> SetActiveStatusAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken = default);

        Task<UserDto?> GetCurrentUserAsync(
            CancellationToken cancellationToken = default);
    }
}