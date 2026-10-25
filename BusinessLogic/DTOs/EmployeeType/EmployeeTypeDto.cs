namespace BusinessLogic.DTOs.EmployeeType
{
    public class EmployeeTypeDto
    {
        public int EmployeeTypeId { get; set; }
        public string TypeName { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string? Description { get; set; }
    }
}