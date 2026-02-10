using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Online_Store_Application.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUnitOfWork _unitOfWork;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor, IUnitOfWork unitOfWork)
        {
            _httpContextAccessor = httpContextAccessor;
            _unitOfWork = unitOfWork;
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
                return 0;

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : 0;
        }


        public int GetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                throw new Exception("کاربر احراز هویت نشده است.");

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new Exception($"UserId claim not found or invalid: {userIdClaim}");

            return userId;
        }

        public string? GetCurrentUserRole()

        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.Role)?.Value;
        }

        public async Task<int> GetCurrentCustomerId()
        {
            var userId = GetCurrentUserId();

            var customerId = await _unitOfWork.Customer.Query()
                                .Where(c => c.UserId == userId)
                                .Select(c => (int?)c.CustomerId)
                                .FirstOrDefaultAsync();

            return customerId ?? throw new Exception($"Customer not found for user with ID: {userId}");
        }
    }
}
