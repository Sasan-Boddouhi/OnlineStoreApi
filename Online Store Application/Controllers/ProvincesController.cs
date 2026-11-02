using Application.Common.Queries;
using Application.Entities;
using Asp.Versioning;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Provinces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProvincesController : ControllerBase
{
    private readonly IProvinceService _provinceService;

    private static readonly QueryParseContext<Province> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(ProvinceQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public ProvincesController(IProvinceService provinceService)
    {
        _provinceService = provinceService;
    }

    /// <summary>جستجو و دریافت فهرست استان‌ها با فیلتر و صفحه‌بندی.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProvinceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var parseResult = StringQueryParser.TryParse<Province>(
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

        var result = await _provinceService.GetByQueryAsync(validation.Value!, cancellationToken);
        return Ok(result);
    }

    /// <summary>دریافت مشخصات استان بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProvinceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _provinceService.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>ایجاد استان جدید توسط ادمین.</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProvinceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateProvinceDto dto,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _provinceService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.ProvinceId }, created);
    }

    /// <summary>ویرایش مشخصات استان توسط ادمین.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProvinceDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.ProvinceId.HasValue && id != dto.ProvinceId.Value)
            return BadRequest("ID mismatch");

        dto.ProvinceId = id;

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _provinceService.UpdateAsync(dto, cancellationToken);
        if (updated == null)
            return NotFound();

        return NoContent();
    }

    /// <summary>حذف استان توسط ادمین.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _provinceService.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
