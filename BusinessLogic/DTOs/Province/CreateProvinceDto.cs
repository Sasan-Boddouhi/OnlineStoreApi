using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Province
{
    public class CreateProvinceDto
    {
        [Required]
        [MaxLength(100)]
        public string? ProvinceName { get; set; }
    }
}
