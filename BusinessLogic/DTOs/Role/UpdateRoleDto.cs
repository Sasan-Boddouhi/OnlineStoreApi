namespace BusinessLogic.DTOs.Role
{
    public class UpdateRoleDto
    {
        public int RoleId { get; set; }
        public string Code { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public List<int> PermissionIds { get; set; } = new();
    }
}