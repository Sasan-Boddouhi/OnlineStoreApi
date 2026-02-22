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
    public class UserProjectionSpecification : BaseProjectionSpecification<User, UserDto>
    {
        public UserProjectionSpecification(string? search, string? sortBy, bool ascending, int? skip, int? take)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                Criteria = u => u.FirstName.Contains(search) ||
                                u.LastName.Contains(search) ||
                                u.PhoneNumber.Contains(search);
            }

            ApplySorting(sortBy, ascending);

            if (skip.HasValue && take.HasValue)
                ApplyPaging(skip.Value, take.Value);

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
                case "lastname":
                    if (ascending) ApplyOrderBy(u => u.LastName);
                    else ApplyOrderByDescending(u => u.LastName);
                    break;
                case "phonenumber":
                    if (ascending) ApplyOrderBy(u => u.PhoneNumber);
                    else ApplyOrderByDescending(u => u.PhoneNumber);
                    break;
                default:
                    if (ascending) ApplyOrderBy(u => u.UserId);
                    else ApplyOrderByDescending(u => u.UserId);
                    break;
            }
        }
    }


}
