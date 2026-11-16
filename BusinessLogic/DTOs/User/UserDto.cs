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

    /// <summary>Batch 44: حساب سیستمی بی‌اثر (مثلاً مالک نشانی انبار پلتفرم)؛ قابل ورود، فعال‌سازی یا نقش‌دهی نیست.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Batch 46: شماره با OTP تأیید شده است (کاربران پیش از Batch 46 تأییدشده حساب می‌شوند).</summary>
    public bool PhoneNumberConfirmed { get; set; }

    public DateTime? DateOfBirth { get; set; }
    public string? DateOfBirthPersian { get; set; }

    public UserType UserType { get; set; }

    public int? EmployeeTypeId { get; set; }
    public string? EmployeeTypeName { get; set; }
    public string? EmployeeTypeDisplayName { get; set; }

    public List<AddressDto> Addresses { get; set; } = new();

    public List<string> Roles { get; set; } = new();
}