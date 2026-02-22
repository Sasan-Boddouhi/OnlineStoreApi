using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Users
{
    public class UserByPhoneSpecification : BaseSpecification<User>
    {
        public UserByPhoneSpecification(string phoneNumber, bool includeInactive = false)
        {
            if (includeInactive)
                Criteria = u => u.PhoneNumber == phoneNumber;
            else
                Criteria = u => u.PhoneNumber == phoneNumber && u.IsActive;
        }
    }
}
