using System;
using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Employee
{
    public class CreateEmployeeDto
    {
        public decimal Salary { get; set; }
        public DateTime HireDate { get; set; }
        public string EmployeeNumber { get; set; } = null!;
        public int EmployeeTypeId { get; set; }
        public int UserId { get; set; }
    }
}