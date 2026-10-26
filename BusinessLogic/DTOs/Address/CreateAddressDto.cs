using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Address;

public class CreateAddressDto
{
    [Range(1, int.MaxValue, ErrorMessage = "CityId must be a positive integer.")]
    public int CityId { get; set; }

    [Required]
    [MaxLength(10)]
    public string Plaque { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Unit { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string PostalCode { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string RecipientFirstName { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string RecipientLastName { get; set; } = null!;

    [MaxLength(250)]
    public string? ExtraDescription { get; set; }

    public bool IsDefault { get; set; }
}