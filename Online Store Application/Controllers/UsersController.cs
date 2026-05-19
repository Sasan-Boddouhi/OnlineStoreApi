using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Online_Store_Application.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    private static readonly QueryParseContext<User> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(UserQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    // GET: api/users?filter=...&sort=...&pageNumber=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var parseResult = StringQueryParser.TryParse<User>(
                filter, sort, QueryContext, pageNumber, pageSize);

            if (!parseResult.Success)
            {
                return BadRequest(new
                {
                    Title = "Invalid query syntax",
                    Errors = parseResult.Errors.Select(e => new { e.Code, e.Message, e.Target })
                });
            }

            var normalized = QueryPolicy.Normalize(parseResult.Value!, QueryContext);
            var validation = QueryPolicy.Validate(normalized, QueryContext);

            if (!validation.Success)
            {
                return BadRequest(new
                {
                    Title = "Invalid query values",
                    Errors = validation.Errors.Select(e => new { e.Code, e.Message, e.Target })
                });
            }

            var result = await _userService.GetByQueryAsync(validation.Value!);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /users failed");
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    // GET: api/users/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUserById(int id, [FromQuery] bool includeRoles = false)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id, includeRoles);
            if (user == null) return NotFound();
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /users/{Id} failed", id);
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    // POST: api/users
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var created = await _userService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetUserById), new { id = created.UserId }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /users failed");
            return BadRequest(ex.Message);
        }
    }

    // PUT: api/users/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
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

    // PATCH: api/users/{id}
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> PatchUserStatus(int id, [FromBody] UpdateUserStatusDto dto)
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

    // DELETE: api/users/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
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

    // GET: api/users/me
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _userService.GetByIdAsync(userId, includeRoles: true);
        if (user == null) return NotFound();
        return Ok(user);
    }
}