using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.EmployeeTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class EmployeeTypesController : ControllerBase
{
    private readonly IEmployeeTypeService _employeeTypeService;
    private readonly ILogger<EmployeeTypesController> _logger;

    private static readonly QueryParseContext<EmployeeType> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(EmployeeTypeQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public EmployeeTypesController(
        IEmployeeTypeService employeeTypeService,
        ILogger<EmployeeTypesController> logger)
    {
        _employeeTypeService = employeeTypeService;
        _logger = logger;
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        var parseResult = StringQueryParser.TryParse<EmployeeType>(
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

        var result = await _employeeTypeService.GetByQueryAsync(validation.Value!);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _employeeTypeService.GetByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeTypeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _employeeTypeService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.EmployeeTypeId }, created);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeTypeDto dto)
    {
        if (id != dto.EmployeeTypeId)
            return BadRequest("ID mismatch");
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _employeeTypeService.UpdateAsync(dto);
        if (updated == null)
            return NotFound();
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _employeeTypeService.DeleteAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}
