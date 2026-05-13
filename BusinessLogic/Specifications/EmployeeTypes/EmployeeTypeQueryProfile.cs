using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;

namespace BusinessLogic.Specifications.EmployeeTypes;

public static class EmployeeTypeQueryProfile
{
    public static readonly string[] AllowedFields = { "EmployeeTypeId", "TypeName", "Description" };

    public static QueryProfile<EmployeeType, EmployeeTypeDto> Profile { get; } = new()
    {
        AllowedFields = AllowedFields,
        Projection = et => new EmployeeTypeDto
        {
            EmployeeTypeId = et.EmployeeTypeId,
            TypeName = et.TypeName,
            Description = et.Description
        }
    };
}