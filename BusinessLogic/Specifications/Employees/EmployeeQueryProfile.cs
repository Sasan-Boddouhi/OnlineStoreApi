using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Employee;

namespace BusinessLogic.Specifications.Employees;

public static class EmployeeQueryProfile
{
    public static readonly string[] AllowedFields =
    {
        "EmployeeId", "UserId", "EmployeeTypeId", "EmployeeNumber",
        "HireDate", "TerminationDate", "Salary"
    };

    public static QueryProfile<Employee, EmployeeDto> Profile { get; } = new()
    {
        Includes =
        {
            e => e.User,
            e => e.EmployeeType
        },
        AllowedFields = AllowedFields,
        Projection = e => new EmployeeDto
        {
            EmployeeId = e.EmployeeId,
            UserId = e.UserId,
            UserFullName = e.User.FullName,
            PhoneNumber = e.User.PhoneNumber,
            EmployeeTypeId = e.EmployeeTypeId,
            EmployeeTypeName = e.EmployeeType.TypeName,
            EmployeeNumber = e.EmployeeNumber,
            HireDate = e.HireDate,
            TerminationDate = e.TerminationDate,
            Salary = e.Salary
        }
    };
}