using Application.Common.Queries;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    private static readonly QueryParseContext<Product> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(ProductQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var parseResult = StringQueryParser.TryParse<Product>(
            filter,
            sort,
            QueryContext,
            page: pageNumber,
            size: pageSize);

        if (!parseResult.Success)
        {
            return BadRequest(new
            {
                Title = "Invalid query syntax",
                Errors = parseResult.Errors
            });
        }

        var normalized = QueryPolicy.Normalize(
            parseResult.Value!,
            QueryContext);

        var validation = QueryPolicy.Validate(
            normalized,
            QueryContext);

        if (!validation.Success)
        {
            return BadRequest(new
            {
                Title = "Invalid query values",
                Errors = validation.Errors
            });
        }

        var result = await _productService
            .GetByQueryAsync(validation.Value!);

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        var product = await _productService.GetByIdAsync(id);

        if (product == null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Employee")]
    public async Task<ActionResult<ProductDto>> CreateProduct(
        CreateProductDto dto)
    {
        var created = await _productService.CreateAsync(dto);

        return CreatedAtAction(
            nameof(GetProduct),
            new { id = created.ProductId },
            created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Employee")]
    public async Task<ActionResult<ProductDto>> UpdateProduct(
        int id,
        UpdateProductDto dto)
    {
        if (id != dto.ProductId)
        {
            return BadRequest();
        }

        var updated = await _productService.UpdateAsync(dto);

        if (updated == null)
        {
            return NotFound();
        }

        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Employee")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var success = await _productService.DeleteAsync(id);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}