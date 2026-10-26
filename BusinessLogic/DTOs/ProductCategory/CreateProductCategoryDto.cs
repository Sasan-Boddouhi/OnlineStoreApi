using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.ProductCategory
{
    public class CreateProductCategoryDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }
}