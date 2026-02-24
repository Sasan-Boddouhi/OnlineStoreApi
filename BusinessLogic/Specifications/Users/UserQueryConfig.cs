using Application.Entities;
using BusinessLogic.DTOs.User;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Users
{
    public static class UserQueryConfig
    {
        public static readonly string[] AllowedFields =
        {
            "firstname",
            "lastname",
            "phonenumber",
            "email",
            "isactive",
            "employee.employeetype.typename"
        };

        public static Expression<Func<User, UserDto>> Projection =>
            u => new UserDto
            {
                UserId = u.UserId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive,
                // در صورت وجود Employee و EmployeeType
                RoleName = u.Employee != null && u.Employee.EmployeeType != null
                    ? u.Employee.EmployeeType.TypeName
                    : (u.UserType == UserType.Customer ? "Customer" : "NoRole")
            };
    }
}