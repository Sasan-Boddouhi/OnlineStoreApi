using Application.Entities;
using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Product
{
    public class CreateProductDto
    {
        [Required(ErrorMessage = "نام محصول الزامی است.")]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "قیمت محصول الزامی است.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "قیمت باید بزرگتر از صفر باشد.")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "واحد اندازه‌گیری الزامی است.")]
        public UnitOfMeasurement Unit { get; set; }

        [Required(ErrorMessage = "زیردسته‌بندی باید انتخاب شود.")]
        public int SubcategoryId { get; set; }

        [MaxLength(20)]
        public string? Barcode { get; set; }

        [MaxLength(250)]
        public string? Description { get; set; }

        public string? ImageUrl { get; set; }
        public DateTime? ExpirationDate { get; set; }
    }
}