using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace OnlineStore.Tests.Integration;

/// <summary>
/// کاربر جاری قابل تنظیم، تا تست‌های مالکیت بتوانند خود را جای کاربر واقعی بگذارند.
/// </summary>
public sealed class FakeCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public FakeCurrentUserService(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int CurrentUserId { get; set; } = 1;

    public string CurrentUserName { get; set; } = "TestUser";

    public string? CurrentUserRole { get; set; } = "Admin";

    public bool Authenticated { get; set; } = true;

    public int GetCurrentUserId() => TryGetCurrentUserId() ?? CurrentUserId;

    public int? TryGetCurrentUserId()
    {
        var httpUser = _httpContextAccessor?.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true)
        {
            var idClaim = httpUser.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? httpUser.FindFirst("sub")?.Value;
            if (int.TryParse(idClaim, out var id))
                return id;
        }

        return CurrentUserId;
    }

    public string GetCurrentUserName()
    {
        var httpUser = _httpContextAccessor?.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(httpUser.Identity.Name))
            return httpUser.Identity.Name;

        return CurrentUserName;
    }

    public string? GetCurrentUserRole()
    {
        var httpUser = _httpContextAccessor?.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true)
        {
            var roleClaim = httpUser.FindFirst(ClaimTypes.Role)?.Value
                         ?? httpUser.FindFirst("role")?.Value;
            if (!string.IsNullOrEmpty(roleClaim))
                return roleClaim;
        }

        return CurrentUserRole;
    }

    public bool IsAuthenticated()
    {
        var httpUser = _httpContextAccessor?.HttpContext?.User;
        if (httpUser?.Identity != null)
            return httpUser.Identity.IsAuthenticated;

        return Authenticated;
    }
}
