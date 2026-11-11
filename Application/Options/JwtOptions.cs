using System.ComponentModel.DataAnnotations;

namespace Application.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required(ErrorMessage = "Jwt:Key is required.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters.")]
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Batch 11 (F-13): طول عمر access token. کوتاه است چون نشست با refresh زنده می‌ماند
    /// (<see cref="UserSessionOptions"/>)؛ کلاینت باید پیش از انقضا یا پس از 401 refresh کند.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Jwt:ExpireMinutes must be between 1 and 1440.")]
    public int ExpireMinutes { get; set; } = 15;
}
