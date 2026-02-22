using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IUserService
    {
        // CRUD اصلی
        Task<PagedResult<UserDto>> GetPagedAsync(int pageNumber, int pageSize, string? search = null, string? sortBy = "UserId", bool ascending = true);
        Task<PagedResult<UserDto>> GetPagedWithRolesAsync(int pageNumber, int pageSize, string? search = null, string? sortBy = "UserId", bool ascending = true);
        Task<UserDto?> GetByIdAsync(int id);
        Task<UserDto?> GetByIdWithRoleNameAsync(int id);
        Task<IEnumerable<UserDto>> GetAllAsync(string? search = null);
        Task<IEnumerable<UserDto>> GetAllUsersWithRoleNameAsync(string? search = null);
        Task<IEnumerable<string>> GetAllRolesAsync();
        Task<List<string>> GetRolesByUserTypeAsync(string? userType);

        Task<UserDto> CreateAsync(CreateUserDto dto);
        Task<UserDto?> UpdateAsync(UpdateUserDto dto);
        Task<bool> DeleteAsync(int id);

        // متدهای پرکاربرد اضافی
        Task<UserDto?> GetByPhoneNumberAsync(string username);                  // گرفتن کاربر با Username
        Task<bool> SetActiveStatusAsync(int id, bool isActive);              // فعال/غیرفعال کردن کاربر
        Task<UserDto?> GetCurrentUserAsync();                                // گرفتن کاربر لاگین‌شده
    }
}
