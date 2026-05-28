using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.EmployeeType
{
    public class UpdateEmployeeTypeDto
    {
        public int EmployeeTypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsActive { get; set; }
    }
}