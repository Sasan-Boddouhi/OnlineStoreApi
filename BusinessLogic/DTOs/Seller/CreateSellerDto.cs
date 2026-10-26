using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Seller
{
    public class CreateSellerDto
    {
        public int UserId { get; set; }

        [Required]
        [MaxLength(150)]
        public string StoreName { get; set; } = null!;

        [MaxLength(150)]
        public string? Slug { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }
    }
}