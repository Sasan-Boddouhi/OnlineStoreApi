using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Employee
{
    public class CreateEmployeeDto
    {
        public decimal Salary { get; set; }

        public DateTime HireDate { get; set; }

        public string EmployeeNumber { get; set; }

        public int EmployeeTypeId { get; set; }

        public int UserId { get; set; }
    }
}
