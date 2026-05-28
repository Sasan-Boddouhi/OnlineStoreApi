namespace BusinessLogic.DTOs.Address;

public class AddressDto
{
    public int AddressId { get; set; }

    public int CityId { get; set; }

    public string CityName { get; set; } 

    public int UserId { get; set; } 

    public string Plaque { get; set; }

    public string Unit { get; set; }

    public string PostalCode { get; set; }

    public string RecipientFirstName { get; set; }

    public string RecipientLastName { get; set; }

    public bool IsDefault { get; set; }
}