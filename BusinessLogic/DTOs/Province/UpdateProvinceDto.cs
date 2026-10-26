using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Province
{
    public class UpdateProvinceDto
    {
        [Required]
        public int? ProvinceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string ProvinceName { get; set; } = null!;
    }
}