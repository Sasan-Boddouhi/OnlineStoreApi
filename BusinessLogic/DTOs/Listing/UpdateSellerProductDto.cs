using Application.Entities;

namespace BusinessLogic.DTOs.Listing
{
    public class UpdateSellerProductDto
    {
        public decimal Price { get; set; }
        public SellerProductStatus Status { get; set; }
    }
}