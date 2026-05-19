using Application.Entities;
using Application.Helper;
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
    public static Expression<Func<User, UserDto>> SimpleProjection =>
        u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            Email = u.Email,
            DateOfBirth = u.DateOfBirth != DateTime.MinValue
                ? PersianDateHelper.ToPersian(u.DateOfBirth)
                : null
        };

    public static Expression<Func<User, UserDto>> ProjectionWithRole =>
        u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            Email = u.Email,
            DateOfBirth = u.DateOfBirth != DateTime.MinValue
                ? PersianDateHelper.ToPersian(u.DateOfBirth)
                : null,
            RoleName = u.Employee != null && u.Employee.EmployeeType != null
                ? u.Employee.EmployeeType.TypeName
                : (u.UserType == UserType.Customer ? "Customer" : "NoRole")
        };
}