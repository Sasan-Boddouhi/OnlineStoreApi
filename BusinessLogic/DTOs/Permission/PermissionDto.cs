namespace BusinessLogic.DTOs.Permission
{
    public class PermissionDto
    {
        public int PermissionId { get; set; }
        public string Code { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string? Group { get; set; }
    }
}