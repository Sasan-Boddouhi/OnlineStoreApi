namespace BusinessLogic.DTOs.User;

public sealed record UserProfileDto(
    string UserId,
    string FullName,
    string Role,
    string PhoneNumber
);