using System.Text.Json;

namespace BusinessLogic.DTOs.User;

/// <summary>
/// R45 — بدنهٔ انتساب یک‌بارهٔ نقش‌ها به یک کاربر (جایگزین کامل).
/// </summary>
public class AssignRolesDto
{
    /// <summary>شناسهٔ نقش‌هایی که باید به کاربر انتساب شوند (جایگزین نقش‌های فعلی).</summary>
    public List<int> RoleIds { get; set; } = new();
}
