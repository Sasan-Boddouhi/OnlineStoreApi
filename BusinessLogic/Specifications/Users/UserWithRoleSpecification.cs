using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Users
{
    public class UserWithRoleSpecification : BaseSpecification<User>
    {
        public UserWithRoleSpecification(int userId)
        {
            AddCriteria(u => u.UserId == userId);
            AddInclude(u => u.Employee);
            AddInclude(u => u.Employee.EmployeeType);
            ApplyNoTracking(); // فقط خواندنی (برای تولید توکن)
        }
    }
}
