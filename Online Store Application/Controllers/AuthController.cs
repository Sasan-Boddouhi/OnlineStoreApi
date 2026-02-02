using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

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
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            try
            {
                var result = await _authService.RegisterAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        // ورود و دریافت AccessToken + RefreshToken
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
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
            return Ok(new
            {
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                FullName = User.FindFirst("FullName")?.Value,
                Role = User.FindFirst(ClaimTypes.Role)?.Value,
                PhoneNumber = User.FindFirst("PhoneNumber")?.Value
            });
        }

        // تازه‌سازی توکن با RefreshToken
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(RefreshTokenDto dto)
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
            // گرفتن UserId از توکن
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            await _authService.LogoutAsync(userId);

            return NoContent();
        }

    }
}
