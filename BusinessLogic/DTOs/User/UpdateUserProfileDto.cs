using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.User;

public class UpdateUserProfileDto
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = null!;

    public string? DateOfBirth { get; set; }
}
