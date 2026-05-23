using Application.Entities;
using BusinessLogic.DTOs.Address;
using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.User
{
    public class UserDto
    {
        public int UserId { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string FullName => $"{FirstName} {LastName}";

        public string PhoneNumber { get; set; } = string.Empty;

        public DateTime? DateOfBirth { get; set; }

        public string? DateOfBirthPersian { get; set; }

        public bool IsActive { get; set; }

        public string RoleName { get; set; } = string.Empty;

        public string UserTypeName { get; set; } = string.Empty;

        public string? Email { get; set; }

        public List<AddressDto> Addresses { get; set; } = [];

        public string RoleDisplayName => RoleName switch
        {
            "Admin" => "ادمین",
            "Manager" => "مدیر",
            "" => "بدون نقش",
            null => "بدون نقش",
            _ => RoleName
        };

        public string UserTypeDisplayName =>
            Enum.TryParse<UserType>(UserTypeName, out var type)
                ? type.GetDisplayName()
                : "نامشخص";
    }
}
