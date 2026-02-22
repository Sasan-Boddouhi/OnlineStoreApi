using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Users
{
    public class UserCountSpecification : BaseSpecification<User>
    {
        public UserCountSpecification(string? search)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                Criteria = u => u.FirstName.Contains(search) ||
                                u.LastName.Contains(search) ||
                                u.PhoneNumber.Contains(search);
            }
        }
    }
}
