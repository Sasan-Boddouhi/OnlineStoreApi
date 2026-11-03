using Asp.Versioning;
﻿using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLogic.DTOs.User;

namespace WebApi.Controllers
{
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/v{version:apiVersion}/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>ثبت‌نام کاربر جدید.</summary>
        [HttpPost("register")]
        [EnableRateLimiting("LoginLimiter")]
        [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var result =
                await _authService.RegisterAsync(dto);

            return Ok(result);
        }

        /// <summary>ورود کاربر و دریافت AccessToken و RefreshToken.</summary>
        [HttpPost("login")]
        [EnableRateLimiting("LoginLimiter")]
        [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var tokenResult = await _authService.LoginAsync(dto);
            if (tokenResult == null)
                return Unauthorized(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "شماره تماس یا رمز عبور اشتباه است",
                    Instance = HttpContext.Request.Path
                });

            return Ok(tokenResult);
        }

        /// <summary>دریافت اطلاعات پروفایل کاربر احراز هویت شده.</summary>
        [Authorize]
        [HttpGet("me")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var fullName = User.FindFirstValue("FullName") ?? string.Empty;
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";
            var phone = User.FindFirstValue("PhoneNumber") ?? string.Empty;

            var profile = new UserProfileDto(userId, fullName, role, phone);
            return Ok(profile);
        }

        /// <summary>تازه‌سازی توکن احراز هویت با استفاده از RefreshToken.</summary>
        [HttpPost("refresh")]
        [EnableRateLimiting("RefreshLimiter")]
        [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            var tokenResult = await _authService.RefreshTokenAsync(dto.RefreshToken);
            if (tokenResult == null)
                return Unauthorized(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "توکن منقضی یا نامعتبر است",
                    Instance = HttpContext.Request.Path
                });

            return Ok(tokenResult);
        }

        /// <summary>خروج از نشست جاری و ابطال توکن‌ها.</summary>
        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
