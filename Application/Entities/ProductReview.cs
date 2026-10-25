using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 5 — نظر روی محصول (فقط خرید تأییدشده).
    ///
    /// OrderItemId اجباری است: بدون آن نمی‌توان اثبات کرد کاربر واقعاً خریده است.
    /// IsApproved پیش‌فرض false است تا نظر پیش از نمایش بازبینی شود.
    /// </summary>
    [Table("ProductReview")]
    public class ProductReview : AuditableEntity
    {
        public const int MinRating = 1;
        public const int MaxRating = 5;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProductReviewId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        [Required]
        public int CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer Customer { get; set; } = null!;

        /// <summary>آیتم سفارشی که خرید را اثبات می‌کند.</summary>
        [Required]
        public int OrderItemId { get; set; }

        [ForeignKey(nameof(OrderItemId))]
        public virtual OrderItem OrderItem { get; set; } = null!;

        /// <summary>امتیاز ۱ تا ۵.</summary>
        [Range(MinRating, MaxRating)]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public bool IsApproved { get; set; }

        public void SetRating(int rating)
        {
            if (rating < MinRating || rating > MaxRating)
                throw new ArgumentOutOfRangeException(nameof(rating), $"امتیاز باید بین {MinRating} و {MaxRating} باشد.");

            Rating = rating;
        }
    }
}