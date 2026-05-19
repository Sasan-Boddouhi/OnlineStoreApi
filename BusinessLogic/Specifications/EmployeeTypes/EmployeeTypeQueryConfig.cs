using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.EmployeeTypes;

public static class EmployeeTypeQueryConfig
{
    public static readonly string[] AllowedFields = { "EmployeeTypeId", "TypeName", "Description" };

    public static Expression<Func<EmployeeType, EmployeeTypeDto>> Projection =>
        et => new EmployeeTypeDto
        {
            EmployeeTypeId = et.EmployeeTypeId,
            TypeName = et.TypeName,
            Description = et.Description
        };
}