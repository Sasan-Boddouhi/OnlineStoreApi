using Application.Interfaces;

namespace OnlineStore.Tests.Integration;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public int GetCurrentUserId() => 1;

    public int? TryGetCurrentUserId() => 1;

    public string? GetCurrentUserName() => "TestUser";

    public string? GetCurrentUserRole() => "Admin";

    public bool IsAuthenticated() => true;
}