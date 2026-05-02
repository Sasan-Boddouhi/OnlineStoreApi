using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeTypesController : ControllerBase
    {
        private readonly IEmployeeTypeService _employeeTypeService;
        private readonly ILogger<EmployeeTypesController> _logger;

        public EmployeeTypesController(IEmployeeTypeService employeeTypeService, ILogger<EmployeeTypesController> logger)
        {
            _employeeTypeService = employeeTypeService;
            _logger = logger;
        }

        // GET: api/employeetypes?search=&sortBy=EmployeeTypeId&ascending=true&pageNumber=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> GetAllAsync(
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? sortBy = "EmployeeTypeId",
            [FromQuery] bool ascending = true)
        {
            try
            {
                var result = await _employeeTypeService.GetAllAsync(search, sortBy, pageNumber, pageSize);
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
        public async Task<IActionResult> GetByIdAsync(int id)
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
        public async Task<IActionResult> CreateAsync([FromBody] CreateEmployeeTypeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var created = await _employeeTypeService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetByIdAsync), new { id = created.EmployeeTypeId }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST /employeetypes failed");
                return BadRequest(ex.Message);
            }
        }

        // PUT: api/employeetypes/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAsync(int id, [FromBody] UpdateEmployeeTypeDto dto)
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
        public async Task<IActionResult> DeleteAsync(int id)
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
}