using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Listing;

public class UpdateListingPriceRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero.")]
    public decimal Price { get; set; }
}
