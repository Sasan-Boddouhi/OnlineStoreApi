using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Application.Entities
{
    /// <summary>
    /// نقش امنیتی (Phase 1). Code عیناً برابر مقدار قدیمی EmployeeType.TypeName است
    /// تا چک‌های رشته‌ای موجود (مثل ProductCategoryService.EnsureAdmin) بی‌صدا نشکنند (D7).
    /// </summary>
    [Table("Role")]
    [Index(nameof(Code), IsUnique = true)]
    public class Role : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int RoleId { get; set; }

        [Required]
        [StringLength(50)]
        public required string Code { get; set; }

        [Required]
        [StringLength(100)]
        public required string DisplayName { get; set; }

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(200)]
        public string? Description { get; set; }

        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new HashSet<RolePermission>();

        public virtual ICollection<UserRole> UserRoles { get; set; } = new HashSet<UserRole>();
    }
}