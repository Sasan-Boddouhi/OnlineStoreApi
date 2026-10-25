using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// رکورد حسابرسی فقط-افزودنی (append-only).
    /// در Phase 1 صرفاً شِمای آن ساخته می‌شود؛ نوشتن رکوردها در Phase 5 (Hardening) سیم‌کشی می‌شود.
    /// عمداً از AuditableEntity ارث نمی‌برد: خودش تاریخ رخداد را دارد و هرگز ویرایش نمی‌شود.
    /// </summary>
    [Table("AuditLog")]
    public class AuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long AuditLogId { get; set; }

        public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;

        /// <summary>کاربر انجام‌دهنده؛ null یعنی عملیات سیستمی/پس‌زمینه.</summary>
        public int? UserId { get; set; }

        [Required]
        [StringLength(100)]
        public required string Action { get; set; }

        [StringLength(100)]
        public string? EntityName { get; set; }

        [StringLength(100)]
        public string? EntityId { get; set; }

        [StringLength(45)]
        public string? IpAddress { get; set; }

        public string? Details { get; set; }
    }
}