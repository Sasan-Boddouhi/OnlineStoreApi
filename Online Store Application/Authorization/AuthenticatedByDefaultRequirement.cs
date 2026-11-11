using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Online_Store_Application.Authorization;

/// <summary>
/// Batch 40 (SEC-01): نیازمندی سیاست پیش‌فرض (FallbackPolicy).
/// هر endpoint که هیچ ویژگی مجوزی ندارد (نه [Authorize] و نه [AllowAnonymous]) فقط برای کاربر
/// احراز هویت‌شده باز است؛ پس endpoint تازه‌ای که ویژگی‌اش فراموش شده، ناخواسته عمومی نمی‌شود.
/// درخواستی که به هیچ endpointی نخورده (مسیر ناموجود) از این قید معاف است تا همچنان 404 بگیرد، نه 401.
/// </summary>
public sealed class AuthenticatedByDefaultRequirement
    : AuthorizationHandler<AuthenticatedByDefaultRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AuthenticatedByDefaultRequirement requirement)
    {
        var isAuthenticated = context.User?.Identities.Any(i => i.IsAuthenticated) == true;
        var matchedNoEndpoint = context.Resource is HttpContext httpContext && httpContext.GetEndpoint() is null;

        if (isAuthenticated || matchedNoEndpoint)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
