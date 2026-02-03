using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Online_Store_Application.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IUserService userService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        // --------------------------------------------------
        // GET: api/users?search=ali&includeRoles=true
        // --------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAsync(
            [FromQuery] string? search = null,
            [FromQuery] bool includeRoles = false,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = "UserId",
            [FromQuery] bool ascending = true)
        {
            try
            {
                var pagedResult = includeRoles
                    ? await _userService.GetPagedWithRolesAsync(pageNumber, pageSize, search, sortBy, ascending)
                    : await _userService.GetPagedAsync(pageNumber, pageSize, search, sortBy, ascending);

                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET /users failed");
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }



        // --------------------------------------------------
        // GET: api/users/{id}?includeRoles=true
        // --------------------------------------------------
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetByIdAsync(
            int id,
            [FromQuery] bool includeRoles = false)
        {
            try
            {
                var user = includeRoles
                    ? await _userService.GetByIdWithRoleNameAsync(id)
                    : await _userService.GetByIdAsync(id);

                if (user == null) return NotFound();
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET /users/{Id} failed", id);
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        // --------------------------------------------------
        // POST: api/users
        // --------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var created = await _userService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetByIdAsync), new { id = created.UserId }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST /users failed");
                return BadRequest(ex.Message);
            }
        }

        // --------------------------------------------------
        // PUT: api/users/{id}
        // --------------------------------------------------
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAsync(int id, [FromBody] UpdateUserDto dto)
        {
            if (id != dto.UserId) return BadRequest("ID mismatch");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var updated = await _userService.UpdateAsync(dto);
                if (updated == null) return NotFound();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PUT /users/{Id} failed", id);
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        // --------------------------------------------------
        // PATCH: api/users/{id}
        // Body: { "isActive": true }
        // --------------------------------------------------
        [HttpPatch("{id:int}")]
        public async Task<IActionResult> PatchStatusAsync(int id, [FromBody] UpdateUserStatusDto dto)
        {
            if (id != dto.UserId) return BadRequest("ID mismatch");

            try
            {
                var result = await _userService.SetActiveStatusAsync(id, dto.IsActive);
                if (!result) return NotFound();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PATCH /users/{Id} failed", id);
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        // --------------------------------------------------
        // DELETE: api/users/{id}
        // --------------------------------------------------
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAsync(int id)
        {
            try
            {
                var deleted = await _userService.DeleteAsync(id);
                if (!deleted) return NotFound();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DELETE /users/{Id} failed", id);
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userService.GetByIdWithRoleNameAsync(Int32.Parse(userId));
            if (user == null)
                return NotFound();

            return Ok(user);
        }
    }
}
