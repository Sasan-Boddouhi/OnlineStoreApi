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
    public class UserByPhoneProjectionSpecification : BaseProjectionSpecification<User, UserDto>
    {
        public UserByPhoneProjectionSpecification(string phoneNumber)
        {
            Criteria = u => u.PhoneNumber == phoneNumber;
            Selector = u => new UserDto
            {
                UserId = u.UserId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = u.FirstName + " " + u.LastName,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive
            };
        }
    }
}
