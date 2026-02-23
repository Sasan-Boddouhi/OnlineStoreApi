using Application.Entities;
using BusinessLogic.DTOs.Address;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.User
{
    public class CreateUserDto
    {
        public UserType UserType { get; set; }
        public string PhoneNumber { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string DateOfBirth { get; set; } = null!;
        public List<CreateAddressDto>? Addresses { get; set; } = new();

    }
}
