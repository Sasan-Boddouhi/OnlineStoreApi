using Asp.Versioning;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 0: مدیریت دسته‌بندی محصول.
/// پیش از این، ProductCategoryService هیچ مسیر HTTP نداشت و دسته‌بندی‌ها فقط با SQL/Seed قابل ساخت بودند.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IProductCategoryService _categoryService;

    public CategoriesController(IProductCategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _categoryService.GetAllAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _categoryService.GetByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductCategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _categoryService.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.ProductCategoryId }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductCategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (id != dto.ProductCategoryId)
            return BadRequest("ID mismatch");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _categoryService.UpdateAsync(dto, cancellationToken);
        if (updated == null)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _categoryService.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}