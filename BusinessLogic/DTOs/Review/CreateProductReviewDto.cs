using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Review
{
    public class CreateProductReviewDto
    {
        [Range(1, 5, ErrorMessage = "امتیاز باید بین ۱ تا ۵ باشد.")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }
}