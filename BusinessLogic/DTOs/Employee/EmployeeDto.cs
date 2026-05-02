using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Employee
{
    public class EmployeeDto
    {
        public int EmployeeId { get; set; }
        public int UserId { get; set; }
        public string UserFullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public int EmployeeTypeId { get; set; }
        public string EmployeeTypeName { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public DateTime HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public decimal Salary { get; set; }
        public bool IsActive { get; set; }
    }
}
