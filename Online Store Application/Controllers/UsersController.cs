using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Features.Users.CreateUser;
using BusinessLogic.DTOs.Address;
using BusinessLogic.DTOs.Role;
using BusinessLogic.DTOs.Shared;
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

    private readonly IAddressService _addressService;

    public UsersController(
        IUserService userService,
        IAddressService addressService,
        ISender sender,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _addressService = addressService;
        _sender = sender;
        _logger = logger;
    }

    // GET: api/users?filter=...&sort=...&pageNumber=1&pageSize=20
    /// <summary>جستجو و دریافت فهرست کاربران با فیلتر و صفحه‌بندی (مخصوص ادمین).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// <summary>دریافت مشخصات کاربر بر اساس شناسه (مخصوص ادمین).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(int id, [FromQuery] bool includeRoles = false)
    {
        var user = await _userService.GetByIdAsync(id, includeRoles);
        if (user == null) return NotFound();
        return Ok(user);
    }

    // POST: api/users
    /// <summary>ایجاد کاربر جدید توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
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

    // PUT: api/users/me
    /// <summary>ویرایش مشخصات پروفایل کاربر جاری.</summary>
    [Authorize]
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserProfileDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var updated = await _userService.UpdateProfileAsync(userId, dto, cancellationToken);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    // PUT: api/users/{id}
    /// <summary>ویرایش مشخصات کاربر توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        if (id != dto.UserId) return BadRequest("ID mismatch");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _userService.UpdateAsync(dto);
        if (updated == null) return NotFound();
        return NoContent();
    }

    // PATCH: api/users/{id}
    /// <summary>تغییر وضعیت فعال بودن کاربر توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPatch("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchUserStatus(int id, [FromBody] UpdateUserStatusDto dto)
    {
        if (id != dto.UserId) return BadRequest("ID mismatch");

        var result = await _userService.SetActiveStatusAsync(id, dto.IsActive);
        if (!result) return NotFound();
        return NoContent();
    }

    // DELETE: api/users/{id}
    /// <summary>حذف کاربر توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var deleted = await _userService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // PUT: api/users/{id}/roles
    /// <summary>تخصیص نقش به کاربر توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:int}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignRoles(int id, [FromBody] AssignRolesDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _userService.AssignRolesAsync(id, dto.RoleIds);
        return NoContent();
    }

    // GET: api/users/roles
    /// <summary>دریافت نقش‌ها بر اساس نوع کاربر توسط ادمین.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IEnumerable<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoles([FromQuery] string? userType, CancellationToken cancellationToken)
    {
        var roles = await _userService.GetRolesAsync(userType, cancellationToken);
        return Ok(roles);
    }

    // ==================== Address Endpoints ====================

    // GET: api/users/me/addresses
    /// <summary>دریافت نشانی‌های ثبت‌شده کاربر جاری.</summary>
    [Authorize]
    [HttpGet("me/addresses")]
    [ProducesResponseType(typeof(IEnumerable<AddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyAddresses()
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var addresses = await _addressService.GetAllAsync(userId);
        return Ok(addresses);
    }

    // POST: api/users/me/addresses
    /// <summary>ثبت نشانی جدید برای کاربر جاری.</summary>
    [Authorize]
    [HttpPost("me/addresses")]
    [ProducesResponseType(typeof(AddressDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateMyAddress([FromBody] CreateAddressDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var address = await _addressService.CreateAsync(userId, dto);
        return CreatedAtAction(nameof(GetMyAddresses), new { }, address);
    }

    // PUT: api/users/me/addresses/{addressId}
    /// <summary>ویرایش نشانی کاربر جاری.</summary>
    [Authorize]
    [HttpPut("me/addresses/{addressId:int}")]
    [ProducesResponseType(typeof(AddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMyAddress(int addressId, [FromBody] UpdateAddressDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var address = await _addressService.UpdateAsync(userId, addressId, dto);
        if (address == null) return NotFound();
        return Ok(address);
    }

    // DELETE: api/users/me/addresses/{addressId}
    /// <summary>حذف نشانی کاربر جاری.</summary>
    [Authorize]
    [HttpDelete("me/addresses/{addressId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMyAddress(int addressId)
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var deleted = await _addressService.DeleteAsync(userId, addressId);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // GET: api/users/me
    /// <summary>دریافت مشخصات پروفایل کاربر جاری.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
