using System.ComponentModel.DataAnnotations;

namespace Application.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string Configuration { get; set; } = string.Empty;

    [Required]
    public string InstanceName { get; set; } = "OnlineStore:";
}