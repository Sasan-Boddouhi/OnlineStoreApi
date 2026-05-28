using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.EmployeeType
{
    public class CreateEmployeeTypeDto
    {
        public string TypeName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}