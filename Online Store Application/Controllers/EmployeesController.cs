using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Online_Store_Application.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;
    private readonly ILogger<EmployeesController> _logger;

    // یک نمونه ثابت از QueryParseContext برای Employee
    private static readonly QueryParseContext<Employee> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(EmployeeQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public EmployeesController(IEmployeeService employeeService, ILogger<EmployeesController> logger)
    {
        _employeeService = employeeService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var parseResult = StringQueryParser.TryParse<Employee>(
                filter, sort, QueryContext, pageNumber, pageSize);

            if (!parseResult.Success)
            {
                return BadRequest(new
                {
                    Title = "Invalid query syntax",
                    Errors = parseResult.Errors.Select(e => new { e.Code, e.Message, e.Target })
                });
            }

            // 🔁 Normalize یک QueryContract جدید برمی‌گرداند
            var normalizedContract = QueryPolicy.Normalize(parseResult.Value!, QueryContext);

            // ✅ Validate همان QueryContract را می‌گیرد
            var validation = QueryPolicy.Validate(normalizedContract, QueryContext);

            if (!validation.Success)
            {
                return BadRequest(new
                {
                    Title = "Invalid query values",
                    Errors = validation.Errors.Select(e => new { e.Code, e.Message, e.Target })
                });
            }

            // ارسال QueryContract معتبر به سرویس
            var result = await _employeeService.GetByQueryAsync(validation.Value!);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /employees failed");
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetByIdAsync(int id)
    {
        try
        {
            var employee = await _employeeService.GetByIdAsync(id);
            if (employee == null) return NotFound();
            return Ok(employee);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET /employees/{Id} failed", id);
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateEmployeeDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await _employeeService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetByIdAsync), new { id = created.EmployeeId }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST /employees failed");
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(int id, [FromBody] UpdateEmployeeDto dto)
    {
        if (id != dto.EmployeeId) return BadRequest("ID mismatch");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var updated = await _employeeService.UpdateAsync(dto);
            if (updated == null) return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT /employees/{Id} failed", id);
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        try
        {
            var deleted = await _employeeService.DeleteAsync(id);
            if (!deleted) return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE /employees/{Id} failed", id);
            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var employee = await _employeeService.GetByUserIdAsync(userId);
        if (employee == null)
            return NotFound("Employee record not found for the current user.");

        return Ok(employee);
    }
}