using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLogic.DTOs.User;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        // ثبت‌نام
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var result =
                await _authService.RegisterAsync(dto);

            return Ok(result);
        }


        // ورود و دریافت AccessToken + RefreshToken
        [HttpPost("login")]
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var tokenResult = await _authService.LoginAsync(dto);
            if (tokenResult == null)
                return Unauthorized("شماره تماس یا رمز عبور اشتباه است");

            return Ok(tokenResult);
        }

        // گرفتن اطلاعات کاربر جاری
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var profile = new UserProfileDto(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                User.FindFirstValue("FullName")!,
                User.FindFirstValue(ClaimTypes.Role)!,
                User.FindFirstValue("PhoneNumber")!
            );
            return Ok(profile);
        }

        // تازه‌سازی توکن با RefreshToken
        [HttpPost("refresh")]
        [EnableRateLimiting("RefreshLimiter")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            var tokenResult = await _authService.RefreshTokenAsync(dto.RefreshToken);
            if (tokenResult == null)
                return Unauthorized("توکن منقضی یا نامعتبر است");

            return Ok(tokenResult);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var sessionId = User.FindFirstValue("SessionId");

            if (!Guid.TryParse(sessionId, out var sid))
                return Unauthorized();

            await _authService.LogoutSessionAsync(sid);

            return NoContent();
        }

    }
}
