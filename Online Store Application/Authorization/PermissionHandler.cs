using System.Security.Claims;
using DataLayer.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Online_Store_Application.Authorization;

/// <summary>
/// بررسی می‌کند آیا یکی از نقش‌های کاربر (claim های Role در JWT) در جدول RolePermission
/// مجوز درخواستی را دارد یا نه.
///
/// توجه: این بررسی در هر درخواست به دیتابیس می‌رود تا لغو دسترسی فوری اعمال شود.
/// کش کردن نتیجه یک بهینه‌سازی آینده است، نه بخشی از Phase 1.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _dbContext;

    public PermissionHandler(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var roleCodes = context.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roleCodes.Length == 0)
            return;

        var granted = await _dbContext.RolePermission
            .AsNoTracking()
            .AnyAsync(rp => roleCodes.Contains(rp.Role.Code)
                            && rp.Role.IsActive
                            && rp.Permission.Code == requirement.Permission);

        if (granted)
            context.Succeed(requirement);
    }
}