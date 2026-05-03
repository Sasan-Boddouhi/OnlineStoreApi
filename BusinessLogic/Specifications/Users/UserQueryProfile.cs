using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.User;

namespace BusinessLogic.Specifications.Users;

public static class UserQueryProfile
{
    private static readonly string[] _allowedFields =
    {
        "firstname", "lastname", "phonenumber", "email", "isactive",
        "employee.employeetype.typename"
    };

    public static QueryProfile<User, UserDto> WithRole { get; } = new()
    {
        BaseCriteria = u => u.IsActive,
        Includes = { u => u.Employee.EmployeeType },
        AllowedFields = _allowedFields,
        Projection = u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            RoleName = u.Employee != null && u.Employee.EmployeeType != null
                ? u.Employee.EmployeeType.TypeName
                : (u.UserType == UserType.Customer ? "Customer" : "NoRole")
        }
    };

    public static QueryProfile<User, UserDto> Basic { get; } = new()
    {
        BaseCriteria = u => u.IsActive,
        AllowedFields = _allowedFields,
        Projection = u => new UserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            RoleName = null       // نقش درج نمی‌شود
        }
    };
}