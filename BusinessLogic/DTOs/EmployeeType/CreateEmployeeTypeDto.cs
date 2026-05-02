using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.EmployeeType
{
    public class CreateEmployeeTypeDto
    {
        [Required(ErrorMessage = "نوع کارمند الزامی است.")]
        [MaxLength(50)]
        public string TypeName { get; set; } = null!;

        [MaxLength(200)]
        public string? Description { get; set; }
    }
}