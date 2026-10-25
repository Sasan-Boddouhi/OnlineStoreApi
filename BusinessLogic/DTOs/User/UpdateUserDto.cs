namespace BusinessLogic.DTOs.User;

public class UpdateUserDto
{
    public int UserId { get; set; }

    public string PhoneNumber { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? DateOfBirth { get; set; }

    public string? Email { get; set; }

    public int? EmployeeTypeId { get; set; } 
}