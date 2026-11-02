using BusinessLogic.DTOs.Role;
using BusinessLogic.DTOs.Permission;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;

    public RolesController(IRoleService roleService, IPermissionService permissionService)
    {
        _roleService = roleService;
        _permissionService = permissionService;
    }

    /// <summary>دریافت فهرست تمام نقش‌ها.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _roleService.GetAllAsync();
        return Ok(roles);
    }

    /// <summary>دریافت مشخصات نقش بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var role = await _roleService.GetByIdAsync(id);
        if (role == null) return NotFound();
        return Ok(role);
    }

    /// <summary>دریافت مشخصات نقش بر اساس کد نقش.</summary>
    [HttpGet("code/{code}")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByCode(string code)
    {
        var role = await _roleService.GetByCodeAsync(code);
        if (role == null) return NotFound();
        return Ok(role);
    }

    /// <summary>دریافت فهرست تمام مجوزهای موجود در سیستم.</summary>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IEnumerable<PermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissions()
    {
        var permissions = await _permissionService.GetAllAsync();
        return Ok(permissions);
    }

    /// <summary>دریافت مشخصات مجوز بر اساس شناسه.</summary>
    [HttpGet("permissions/{id:int}")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPermissionById(int id, CancellationToken cancellationToken = default)
    {
        var permission = await _permissionService.GetByIdAsync(id, cancellationToken);
        if (permission == null) return NotFound();
        return Ok(permission);
    }

    /// <summary>ایجاد نقش کاربری جدید.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateRoleDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        
        var role = await _roleService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = role.RoleId }, role);
    }

    /// <summary>ویرایش مشخصات نقش کاربری.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != dto.RoleId) return BadRequest("ID mismatch");

        var role = await _roleService.UpdateAsync(id, dto);
        if (role == null) return NotFound();
        return NoContent();
    }

    /// <summary>حذف نقش کاربری.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _roleService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}