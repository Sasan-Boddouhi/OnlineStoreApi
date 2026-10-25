using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 5 — نظر روی فروشنده.
    ///
    /// برخلاف ProductReview، اینجا OrderItemId نگه داشته نمی‌شود: یک کاربر ممکن است
    /// از یک فروشنده چند سفارش داشته باشد و نظر دربارهٔ «فروشنده» کلی‌تر از یک آیتم است.
    /// خرید تأییدشده هنگام ثبت بررسی می‌شود، ولی ارجاع دقیق ذخیره نمی‌شود.
    /// </summary>
    [Table("SellerReview")]
    public class SellerReview : AuditableEntity
    {
        public const int MinRating = 1;
        public const int MaxRating = 5;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SellerReviewId { get; set; }

        [Required]
        public int SellerId { get; set; }

        [ForeignKey(nameof(SellerId))]
        public virtual Seller Seller { get; set; } = null!;

        [Required]
        public int CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer Customer { get; set; } = null!;

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