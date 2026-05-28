public class CreateAddressDto
{
    public int CityId { get; set; }

    public string Plaque { get; set; } = null!;
    public string Unit { get; set; } = null!;
    public string PostalCode { get; set; } = null!;

    public string RecipientFirstName { get; set; } = null!;
    public string RecipientLastName { get; set; } = null!;

    public string? ExtraDescription { get; set; }

    public bool IsDefault { get; set; }
}