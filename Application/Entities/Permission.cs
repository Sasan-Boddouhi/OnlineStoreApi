using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Application.Entities
{
    /// <summary>
    /// مجوز اتمی (Phase 1). کدها نقطه‌ای و پایدارند؛ هرگز به شناسه عددی تکیه نمی‌کنیم.
    /// </summary>
    [Table("Permission")]
    [Index(nameof(Code), IsUnique = true)]
    public class Permission : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PermissionId { get; set; }

        [Required]
        [StringLength(100)]
        public required string Code { get; set; }

        [Required]
        [StringLength(150)]
        public required string DisplayName { get; set; }

        /// <summary>گروه نمایشی برای رابط مدیریت (مثلاً "Catalog").</summary>
        [StringLength(50)]
        public string? Group { get; set; }

        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new HashSet<RolePermission>();
    }
}