using Application.Entities;
using BusinessLogic.DTOs.User;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Users;

public static class UserQueryConfig
{
    public static readonly string[] AllowedFields =
    [
        "firstname",
        "lastname",
        "phonenumber",
        "email",
        "isactive",
        "employeetype.typename"
    ];

    public static Expression<Func<User, UserDto>> Projection =>
        u => new UserDto
        {
            UserId = u.UserId,

            FirstName = u.FirstName,

            LastName = u.LastName,

            PhoneNumber = u.PhoneNumber,

            IsActive = u.IsActive,

            Email = u.Email,

            DateOfBirth =
                u.DateOfBirth == DateTime.MinValue
                    ? null
                    : u.DateOfBirth,

            RoleName =
                u.Employee != null &&
                u.Employee.EmployeeType != null
                    ? u.Employee.EmployeeType.TypeName
                    : (u.UserType == UserType.Customer
                        ? "Customer"
                        : "NoRole"),

            UserTypeName = u.UserType.ToString()
        };
}