using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.ProductCategory
{
    public class UpdateProductCategoryDto
    {
        [Required]
        public int ProductCategoryId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }
}