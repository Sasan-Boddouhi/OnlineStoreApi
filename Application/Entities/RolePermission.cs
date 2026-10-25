using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// جدول اتصال نقش ↔ مجوز. عمداً از AuditableEntity ارث نمی‌برد (طبق Design Spec §۴.۱).
    /// کلید مرکب (RoleId, PermissionId) در AppDbContext پیکربندی شده است.
    /// </summary>
    [Table("RolePermission")]
    public class RolePermission
    {
        public int RoleId { get; set; }

        [ForeignKey(nameof(RoleId))]
        public virtual Role Role { get; set; } = null!;

        public int PermissionId { get; set; }

        [ForeignKey(nameof(PermissionId))]
        public virtual Permission Permission { get; set; } = null!;
    }
}