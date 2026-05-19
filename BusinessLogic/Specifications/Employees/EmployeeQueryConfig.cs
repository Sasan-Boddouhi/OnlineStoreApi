using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Employee;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Employees;

public static class EmployeeQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "EmployeeId", "UserId", "EmployeeTypeId", "EmployeeNumber",
        "HireDate", "TerminationDate", "Salary"
    };

    public static Expression<Func<Employee, EmployeeDto>> Projection =>
        e => new EmployeeDto
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
        };
}