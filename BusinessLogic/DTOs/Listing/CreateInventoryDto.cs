using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Listing;

public class CreateInventoryDto
{
    [Range(1, int.MaxValue, ErrorMessage = "ListingId must be a positive integer.")]
    public int ListingId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "WarehouseId must be a positive integer.")]
    public int WarehouseId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "InitialQuantity cannot be negative.")]
    public decimal InitialQuantity { get; set; }
}
