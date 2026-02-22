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

        [Required(ErrorMessage = "شماره تماس الزامی است")]
        [RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست (مثال: 09123456789)")]
        public string PhoneNumber { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";

        public string DateOfBirth { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string UserTypeName { get; set; } = string.Empty;

        // Navigation DTOs
        public string? Email { get; set; }
        public List<AddressDto> Addresses { get; set; } = new();

        public string RoleDisplayName => RoleName switch
        {
            "Admin" => "ادمین",
            "Manager" => "مدیر",
            "" => "بدون نقش",
            null => "بدون نقش",
            _ => RoleName
        };

        public string UserTypeDisplayName => Enum.TryParse<UserType>(UserTypeName, out var type)
            ? type.GetDisplayName()
            : "نامشخص";

    }
}
