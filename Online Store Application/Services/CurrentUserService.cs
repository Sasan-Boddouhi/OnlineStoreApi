using Application.Exceptions;
using Application.Interfaces;
using System.Security.Claims;

namespace Online_Store_Application.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetCurrentUserName()
        {
            var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            return !string.IsNullOrEmpty(name) ? name : "Anonymous";
        }

        public int? TryGetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
                return userId;

            return null;
        }


        public int GetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                throw new UnauthorizedException("کاربر احراز هویت نشده است.", "UNAUTHENTICATED");

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedException("شناسه کاربر در توکن معتبر نیست.", "INVALID_USER_CLAIM");

            return userId;
        }

        public string? GetCurrentUserRole()

        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.Role)?.Value;
        }


    }
}
