namespace BusinessLogic.DTOs.Listing;

public class CreateInventoryDto
{
    public int ListingId { get; set; }
    public int WarehouseId { get; set; }
    public decimal InitialQuantity { get; set; }
}
