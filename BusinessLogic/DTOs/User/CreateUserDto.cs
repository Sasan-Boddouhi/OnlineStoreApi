using Application.Entities;

namespace BusinessLogic.DTOs.User;

public class CreateUserDto
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string? Email { get; set; }

    public string? DateOfBirth { get; set; }

    public List<CreateAddressDto>? Addresses { get; set; }
}