using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// جدول اتصال کاربر ↔ نقش. یک کاربر می‌تواند چند نقش داشته باشد (چند-نقشی، Phase 1).
    /// کلید مرکب (UserId, RoleId) در AppDbContext پیکربندی شده است.
    /// </summary>
    [Table("UserRole")]
    public class UserRole
    {
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        public int RoleId { get; set; }

        [ForeignKey(nameof(RoleId))]
        public virtual Role Role { get; set; } = null!;
    }
}