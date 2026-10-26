using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Role
{
    public class CreateRoleDto
    {
        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string DisplayName { get; set; } = null!;

        public bool IsActive { get; set; } = true;

        [MaxLength(200)]
        public string? Description { get; set; }

        public List<int> PermissionIds { get; set; } = new();
    }
}