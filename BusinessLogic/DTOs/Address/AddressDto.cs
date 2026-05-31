namespace BusinessLogic.DTOs.Address;

public class AddressDto
{
    public int AddressId { get; set; }
    public int CityId { get; set; }
    public string CityName { get; set; } = null!;
    public int UserId { get; set; }
    public string Plaque { get; set; } = null!;
    public string Unit { get; set; } = null!;
    public string PostalCode { get; set; } = null!;
    public string RecipientFirstName { get; set; } = null!;
    public string RecipientLastName { get; set; } = null!;
    public bool IsDefault { get; set; }
}