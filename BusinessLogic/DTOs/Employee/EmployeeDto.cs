using System;

namespace BusinessLogic.DTOs.Employee
{
    public class EmployeeDto
    {
        public int EmployeeId { get; set; }
        public int UserId { get; set; }
        public string UserFullName { get; set; } = null!;
        public string PhoneNumber { get; set; } = null!;
        public int EmployeeTypeId { get; set; }
        public string EmployeeTypeName { get; set; } = null!;
        public string EmployeeNumber { get; set; } = null!;
        public DateTime HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public decimal Salary { get; set; }
        public bool IsActive { get; set; }
    }
}