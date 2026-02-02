using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(
            IProductService productService,
            ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        // -------------------------
        // GET all products (public)
        // -------------------------
        [HttpGet]
        public async Task<ActionResult<PagedResult<ProductDto>>> GetAsync(
            [FromQuery] ProductFilterDto filter)
        {
            var result = await _productService.GetFilteredAsync(filter);
            return Ok(result);
        }

        // -------------------------
        // GET product by id (public)
        // -------------------------
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductDto>> GetByIdAsync(int id)
        {
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
                return NotFound();

            return Ok(product);
        }

        // -------------------------
        // CREATE product (protected)
        // -------------------------
        [HttpPost]
        [Authorize(Roles = "Admin,Employee")] // فقط کاربران Admin یا Employee میتونن ایجاد کنن
        public async Task<ActionResult<ProductDto>> CreateAsync(
            [FromBody] CreateProductDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _productService.CreateAsync(dto);

            return CreatedAtAction(
                nameof(GetByIdAsync),
                new { id = created.ProductId },
                created
            );
        }

        // -------------------------
        // UPDATE product (protected)
        // -------------------------
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin,Employee")]
        public async Task<ActionResult<ProductDto>> UpdateAsync(
            int id,
            [FromBody] UpdateProductDto dto)
        {
            if (!ModelState.IsValid || id != dto.ProductId)
                return BadRequest();

            var updated = await _productService.UpdateAsync(dto);
            if (updated == null)
                return NotFound();

            return Ok(updated);
        }

        // -------------------------
        // DELETE product (protected)
        // -------------------------
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin,Employee")]
        public async Task<IActionResult> DeleteAsync(int id)
        {
            var success = await _productService.DeleteAsync(id);
            if (!success)
                return NotFound();

            return NoContent();
        }
    }
}
