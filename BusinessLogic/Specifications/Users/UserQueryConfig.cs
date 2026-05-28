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
            Email = u.Email,
            IsActive = u.IsActive,
            DateOfBirth = u.DateOfBirth,
            UserType = u.UserType,

            EmployeeTypeId = u.Employee != null
                ? u.Employee.EmployeeTypeId
                : null,

            EmployeeTypeName = u.Employee != null && u.Employee.EmployeeType != null
                ? u.Employee.EmployeeType.TypeName
                : null
        };
}