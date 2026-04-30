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

        public ProductsController(IProductService productService, ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<ProductDto>>> GetProducts(
            [FromQuery] string? filter,
            [FromQuery] string? sort,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _productService.GetByQueryAsync(filter, sort, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductDto>> GetProduct(int id)
        {
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
                return NotFound();
            return Ok(product);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Employee")]
        public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _productService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetProduct), new { id = created.ProductId }, created);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin,Employee")]
        public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpdateProductDto dto)
        {
            if (!ModelState.IsValid || id != dto.ProductId)
                return BadRequest();

            var updated = await _productService.UpdateAsync(dto);
            if (updated == null)
                return NotFound();

            return Ok(updated);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin,Employee")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var success = await _productService.DeleteAsync(id);
            if (!success)
                return NotFound();

            return NoContent();
        }
    }
}