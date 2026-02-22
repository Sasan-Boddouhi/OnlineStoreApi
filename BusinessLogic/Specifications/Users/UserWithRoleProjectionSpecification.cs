using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Users
{
    public class UserWithRoleProjectionSpecification : BaseProjectionSpecification<User, UserDto>
    {
        public UserWithRoleProjectionSpecification(string? search, string? sortBy, bool ascending, int pageNumber, int pageSize)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                Criteria = u =>
                    u.FirstName.Contains(search) ||
                    u.LastName.Contains(search) ||
                    u.PhoneNumber.Contains(search) ||
                    (u.Employee != null && u.Employee.EmployeeType != null && u.Employee.EmployeeType.TypeName.Contains(search));
            }

            ApplySorting(sortBy, ascending);

            ApplyPaging((pageNumber - 1) * pageSize, pageSize);

            Selector = u => new UserDto
            {
                UserId = u.UserId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = u.FirstName + " " + u.LastName,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive,
                RoleName = u.Employee != null && u.Employee.EmployeeType != null
                            ? u.Employee.EmployeeType.TypeName
                            : "بدون نقش"
            };
        }

        private void ApplySorting(string? sortBy, bool ascending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                sortBy = "UserId";

            switch (sortBy.ToLower())
            {
                case "firstname":
                    if (ascending) ApplyOrderBy(u => u.FirstName);
                    else ApplyOrderByDescending(u => u.FirstName);
                    break;
                case "role":
                    if (ascending) ApplyOrderBy(u => u.Employee!.EmployeeType!.TypeName);
                    else ApplyOrderByDescending(u => u.Employee!.EmployeeType!.TypeName);
                    break;
                default:
                    if (ascending) ApplyOrderBy(u => u.UserId);
                    else ApplyOrderByDescending(u => u.UserId);
                    break;
            }
        }
    }
}
