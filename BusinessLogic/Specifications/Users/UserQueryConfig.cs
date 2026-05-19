using Application.Entities;
using BusinessLogic.DTOs.User;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Users;

public static class UserQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "firstname",
        "lastname",
        "phonenumber",
        "email",
        "isactive",
        "employeetype.typename"
    };

    // پروجکشن ساده (بدون نقش)
    public static Expression<Func<User, UserDto>> SimpleProjection =>
        u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            Email = u.Email,
            DateOfBirth = u.DateOfBirth.ToString("yyyy/MM/dd") // فرمت دلخواه
        };

    // پروجکشن با نقش (برای نمایش نقش کاربر)
    public static Expression<Func<User, UserDto>> ProjectionWithRole =>
        u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            Email = u.Email,
            DateOfBirth = u.DateOfBirth.ToString("yyyy/MM/dd"),
            RoleName = u.Employee != null && u.Employee.EmployeeType != null
                ? u.Employee.EmployeeType.TypeName
                : (u.UserType == UserType.Customer ? "Customer" : "NoRole")
        };
}