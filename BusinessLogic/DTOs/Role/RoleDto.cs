namespace BusinessLogic.DTOs.Role
{
    public class RoleDto
    {
        public int RoleId { get; set; }
        public string Code { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public List<string> Permissions { get; set; } = new();
    }
}