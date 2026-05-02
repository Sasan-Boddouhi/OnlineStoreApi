using BusinessLogic.DTOs.Employee;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Online_Store_Application.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeesController : ControllerBase
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(IEmployeeService employeeService, ILogger<EmployeesController> logger)
        {
            _employeeService = employeeService;
            _logger = logger;
        }

        // --------------------------------------------------
        // GET: api/employees?search=&pageNumber=1&pageSize=20&sortBy=EmployeeId&ascending=true
        // --------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAllAsync(
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = "EmployeeId",
            [FromQuery] bool ascending = true)
        {
            try
            {
                var result = await _employeeService.GetAllAsync(
                    search, sortBy, pageNumber, pageSize);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET /employees failed");
                return StatusCode(500, "An error occurred while processing the request.");
            }
        }

        // --------------------------------------------------
        // GET: api/employees/{id}
        // --------------------------------------------------
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

        // --------------------------------------------------
        // POST: api/employees
        // --------------------------------------------------
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

        // --------------------------------------------------
        // PUT: api/employees/{id}
        // --------------------------------------------------
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

        // --------------------------------------------------
        // DELETE: api/employees/{id}
        // --------------------------------------------------
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

        // --------------------------------------------------
        // GET: api/employees/me
        // دریافت اطلاعات کارمند مربوط به کاربر لاگین‌شده
        // --------------------------------------------------
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
}