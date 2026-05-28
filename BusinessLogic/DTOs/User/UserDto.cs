using Application.Entities;
using BusinessLogic.DTOs.Address;

namespace BusinessLogic.DTOs.User;

public class UserDto
{
    public int UserId { get; set; }

    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string FullName { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DateOfBirth { get; set; }
    public string? DateOfBirthPersian { get; set; }

    public UserType UserType { get; set; }

    public int? EmployeeTypeId { get; set; }
    public string? EmployeeTypeName { get; set; }
    public string? EmployeeTypeDisplayName { get; set; }

    public List<AddressDto> Addresses { get; set; } = new();
}