using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.EmployeeTypes;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeeTypesController : ControllerBase
{
    private readonly IEmployeeTypeService _employeeTypeService;
    private readonly ILogger<EmployeeTypesController> _logger;

    // QueryParseContext ثابت برای EmployeeType
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

    // GET: api/employeetypes?filter=...&sort=...&pageNumber=1&pageSize=10
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /employeetypes failed");
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    // GET: api/employeetypes/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var result = await _employeeTypeService.GetByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /employeetypes/{Id} failed", id);
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    // POST: api/employeetypes
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeTypeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var created = await _employeeTypeService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.EmployeeTypeId }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /employeetypes failed");
            return BadRequest(ex.Message);
        }
    }

    // PUT: api/employeetypes/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeTypeDto dto)
    {
        if (id != dto.EmployeeTypeId)
            return BadRequest("ID mismatch");
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var updated = await _employeeTypeService.UpdateAsync(dto);
            if (updated == null)
                return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /employeetypes/{Id} failed", id);
            return BadRequest(ex.Message);
        }
    }

    // DELETE: api/employeetypes/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _employeeTypeService.DeleteAsync(id);
            if (!deleted)
                return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /employeetypes/{Id} failed", id);
            return BadRequest(ex.Message);
        }
    }
}