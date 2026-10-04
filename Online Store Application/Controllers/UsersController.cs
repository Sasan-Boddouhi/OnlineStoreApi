using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Features.Users.CreateUser;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ISender _sender;
    private readonly ILogger<UsersController> _logger;

    private static readonly QueryParseContext<User> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(UserQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public UsersController(
        IUserService userService,
        ISender sender,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _sender = sender;
        _logger = logger;
    }

    // GET: api/users?filter=...&sort=...&pageNumber=1&pageSize=20
    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
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

    // GET: api/users/{id}
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUserById(int id, [FromQuery] bool includeRoles = false)
    {
        var user = await _userService.GetByIdAsync(id, includeRoles);
        if (user == null) return NotFound();
        return Ok(user);
    }

    // POST: api/users
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = await _sender.Send(
            new CreateUserCommand(
                dto.FirstName,
                dto.LastName,
                dto.PhoneNumber,
                dto.Password,
                dto.Email,
                dto.DateOfBirth,
                dto.Addresses?
                    .Select(a => new CreateUserAddressCommand(
                        a.CityId,
                        a.Plaque,
                        a.Unit,
                        a.PostalCode,
                        a.RecipientFirstName,
                        a.RecipientLastName,
                        a.ExtraDescription,
                        a.IsDefault))
                    .ToList()));

        var created = await _userService.GetByIdAsync(
            userId,
            includeRoles: true);

        if (created is null)
        {
            throw new BusinessException(
                "خطا در ایجاد کاربر",
                "USER_CREATION_FAILED");
        }

        return CreatedAtAction(
            nameof(GetUserById),
            new { id = created.UserId },
            created);
    }

    // PUT: api/users/{id}
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        if (id != dto.UserId) return BadRequest("ID mismatch");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _userService.UpdateAsync(dto);
        if (updated == null) return NotFound();
        return NoContent();
    }

    // PATCH: api/users/{id}
    [Authorize(Policy = "AdminOnly")]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> PatchUserStatus(int id, [FromBody] UpdateUserStatusDto dto)
    {
        if (id != dto.UserId) return BadRequest("ID mismatch");

        var result = await _userService.SetActiveStatusAsync(id, dto.IsActive);
        if (!result) return NotFound();
        return NoContent();
    }

    // DELETE: api/users/{id}
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var deleted = await _userService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
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
