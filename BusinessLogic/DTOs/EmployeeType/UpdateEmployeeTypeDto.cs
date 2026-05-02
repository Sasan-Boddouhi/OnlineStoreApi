using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.EmployeeType
{
    public class UpdateEmployeeTypeDto
    {
        public int EmployeeTypeId { get; set; }

        [MaxLength(50)]
        public string? TypeName { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }
    }
}