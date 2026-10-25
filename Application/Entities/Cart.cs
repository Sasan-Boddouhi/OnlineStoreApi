using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 4 — سبد خرید.
    ///
    /// مالک سبد یا یک کاربر ثبت‌نام‌شده است (UserId) یا یک نشست ناشناس (SessionId).
    /// دقیقاً یکی از این دو مقدار می‌شود و ایندکس‌های یکتای فیلترشده در AppDbContext این را تضمین می‌کنند.
    /// </summary>
    [Table("Cart")]
    public class Cart : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CartId { get; set; }

        /// <summary>سبد کاربر ثبت‌نام‌شده؛ برای سبد مهمان null است.</summary>
        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }

        /// <summary>شناسهٔ نشست مرورگر برای سبد مهمان؛ پس از ادغام پاک می‌شود.</summary>
        [MaxLength(64)]
        public string? SessionId { get; set; }

        public virtual ICollection<CartItem> Items { get; set; } = new HashSet<CartItem>();

        /// <summary>سبد مهمان است اگر به کاربری تعلق نداشته باشد.</summary>
        [NotMapped]
        public bool IsAnonymous => !UserId.HasValue;

        /// <summary>جمع اقلام سبد. قیمت واحد در CartItem قفل شده است.</summary>
        [NotMapped]
        public decimal TotalAmount => Items.Sum(i => i.UnitPrice * i.Quantity);
    }
}