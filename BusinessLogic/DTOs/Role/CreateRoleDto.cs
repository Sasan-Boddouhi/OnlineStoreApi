namespace BusinessLogic.DTOs.Role
{
    public class CreateRoleDto
    {
        public string Code { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public bool IsActive { get; set; } = true;
        public string? Description { get; set; }
        public List<int> PermissionIds { get; set; } = new();
    }
}