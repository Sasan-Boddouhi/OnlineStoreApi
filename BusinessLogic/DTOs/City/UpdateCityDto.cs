using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.City
{
    public class UpdateCityDto
    {
        [Required]
        public int CityId { get; set; }

        [Required]
        [MaxLength(100)]
        public string CityName { get; set; } = null!;
    }
}