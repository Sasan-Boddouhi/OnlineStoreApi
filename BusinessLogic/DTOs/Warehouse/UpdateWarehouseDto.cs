using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Warehouse
{
    public class UpdateWarehouseDto
    {
        [Required]
        public int WarehouseId { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [Range(1, int.MaxValue, ErrorMessage = "AddressId must be a positive integer.")]
        public int AddressId { get; set; }

        public int? SellerId { get; set; }
    }
}