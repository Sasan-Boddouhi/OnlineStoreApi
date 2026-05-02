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
        [Required(ErrorMessage = "شناسه کاربر الزامی است.")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "شناسه نوع کارمند الزامی است.")]
        public int EmployeeTypeId { get; set; }

        [Required(ErrorMessage = "شماره پرسنلی الزامی است.")]
        [MaxLength(10)]
        public string EmployeeNumber { get; set; } = null!;

        [Required(ErrorMessage = "تاریخ استخدام الزامی است.")]
        public DateTime HireDate { get; set; }

        [Required(ErrorMessage = "حقوق الزامی است.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "حقوق باید بزرگتر از صفر باشد.")]
        public decimal Salary { get; set; }
    }
}
