using Application.Common.Queries;
using Application.Entities;
using Asp.Versioning;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.ProductSubcategories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 0: مدیریت زیردسته‌بندی محصول.
/// IProductSubcategoryService از قبل کامل بود اما هیچ Controllerی آن را در معرض HTTP قرار نمی‌داد.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class SubcategoriesController : ControllerBase
{
    private readonly IProductSubcategoryService _subcategoryService;

    private static readonly QueryParseContext<ProductSubcategory> QueryContext = new()
    {
        AllowedFields = new HashSet<string>(ProductSubcategoryQueryConfig.AllowedFields),
        MaxPageSize = 100,
        CaseInsensitive = true
    };

    public SubcategoriesController(IProductSubcategoryService subcategoryService)
    {
        _subcategoryService = subcategoryService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var parseResult = StringQueryParser.TryParse<ProductSubcategory>(
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

        var result = await _subcategoryService.GetByQueryAsync(validation.Value!, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _subcategoryService.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("by-category/{categoryId:int}/count")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCountByCategory(int categoryId, CancellationToken cancellationToken)
    {
        var count = await _subcategoryService.GetCountByCategoryIdAsync(categoryId, cancellationToken);
        return Ok(new { categoryId, count });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductSubcategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _subcategoryService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.SubcategoryId }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductSubcategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (id != dto.SubcategoryId)
            return BadRequest("ID mismatch");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _subcategoryService.UpdateAsync(dto, cancellationToken);
        if (updated == null)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _subcategoryService.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}