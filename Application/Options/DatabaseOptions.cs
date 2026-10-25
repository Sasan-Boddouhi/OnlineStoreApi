using System.ComponentModel.DataAnnotations;

namespace Application.Options;

public class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required(ErrorMessage = "DefaultConnection string is required.")]
    public string DefaultConnection { get; set; } = string.Empty;
}