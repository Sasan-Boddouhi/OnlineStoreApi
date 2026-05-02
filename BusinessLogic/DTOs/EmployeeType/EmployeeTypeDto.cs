namespace BusinessLogic.DTOs.EmployeeType
{
    public class EmployeeTypeDto
    {
        public int EmployeeTypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}