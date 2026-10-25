using Application.Interfaces;

namespace OnlineStore.Tests.Integration;

/// <summary>
/// کاربر جاری قابل تنظیم، تا تست‌های مالکیت بتوانند خود را جای کاربر واقعی بگذارند.
/// </summary>
public sealed class FakeCurrentUserService : ICurrentUserService
{
    public int CurrentUserId { get; set; } = 1;

    public string CurrentUserName { get; set; } = "TestUser";

    public string? CurrentUserRole { get; set; } = "Admin";

    public bool Authenticated { get; set; } = true;

    public int GetCurrentUserId() => CurrentUserId;

    public int? TryGetCurrentUserId() => CurrentUserId;

    public string GetCurrentUserName() => CurrentUserName;

    public string? GetCurrentUserRole() => CurrentUserRole;

    public bool IsAuthenticated() => Authenticated;
}