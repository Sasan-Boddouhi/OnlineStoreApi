using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Online_Store_Application.Controllers;

using Asp.Versioning;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
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

    // ================= GET ALL =================
    /// <summary>جستجو و دریافت فهرست محصولات با فیلتر، مرتب‌سازی و صفحه‌بندی.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid query syntax",
                Detail = string.Join("; ", parseResult.Errors.Select(e => e.Message)),
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["errors"] = parseResult.Errors;
            return BadRequest(problem);
        }

        var normalized = QueryPolicy.Normalize(
            parseResult.Value!,
            QueryContext);

        var validation = QueryPolicy.Validate(
            normalized,
            QueryContext);

        if (!validation.Success)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid query values",
                Detail = string.Join("; ", validation.Errors.Select(e => e.Message)),
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["errors"] = validation.Errors;
            return BadRequest(problem);
        }

        var result = await _productService.GetByQueryAsync(validation.Value!);
        return Ok(result);
    }

    // ================= GET BY ID =================
    /// <summary>دریافت مشخصات کامل یک محصول بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [OutputCache(Duration = 30, VaryByRouteValueNames = new[] { "id" })]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        var product = await _productService.GetByIdAsync(id);

        if (product is null)
            return NotFound();

        return Ok(product);
    }

    // ================= CREATE =================
    /// <summary>ایجاد محصول جدید در کاتالوگ.</summary>
    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductDto dto)
    {
        try
        {
            var created = await _productService.CreateAsync(dto);

            return CreatedAtAction(
                nameof(GetProduct),
                new { id = created.ProductId },
                created);
        }
        catch (BusinessException ex)
        {
            var problem = new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = "Bad Request",
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = ex.Code;
            return BadRequest(problem);
        }
    }

    // ================= UPDATE =================
    /// <summary>ویرایش مشخصات محصول.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpdateProductDto dto)
    {
        if (id != dto.ProductId)
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "ID mismatch",
                Instance = HttpContext.Request.Path
            });

        try
        {
            var updated = await _productService.UpdateAsync(dto);

            if (updated is null)
                return NotFound();

            return Ok(updated);
        }
        catch (BusinessException ex)
        {
            var problem = new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = "Bad Request",
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = ex.Code;
            return BadRequest(problem);
        }
    }

    // ================= DELETE =================
    /// <summary>حذف نرم محصول.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var success = await _productService.DeleteAsync(id);

        if (!success)
            return NotFound();

        return NoContent();
    }

    // ================= Phase 6: Variants & Attributes =================

    /// <summary>دریافت تنوع‌های یک محصول.</summary>
    [HttpGet("{id:int}/variants")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<ProductVariantDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductVariantDto>>> GetVariants(int id)
        => Ok(await _productService.GetVariantsAsync(id));

    /// <summary>
    /// ساخت واریانت جدید. برای هر محصول باید حداقل یک واریانت وجود داشته باشد،
    /// چون فروشنده فقط می‌تواند روی واریانت پیشنهاد فروش بسازد.
    /// </summary>
    [HttpPost("{id:int}/variants")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(typeof(ProductVariantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductVariantDto>> CreateVariant(
        int id,
        [FromBody] CreateProductVariantDto dto)
    {
        dto.ProductId = id;
        var created = await _productService.CreateVariantAsync(dto);
        return CreatedAtAction(nameof(GetVariants), new { id }, created);
    }

    /// <summary>دریافت مشخصات ویژگی‌های محصول.</summary>
    [HttpGet("{id:int}/attributes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<VariantAttributeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<VariantAttributeDto>>> GetAttributes(
        int id,
        CancellationToken cancellationToken)
        => Ok(await _productService.GetAttributesAsync(id, cancellationToken));

    /// <summary>ایجاد مشخصه یا ویژگی جدید برای محصول.</summary>
    [HttpPost("{id:int}/attributes")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(typeof(VariantAttributeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VariantAttributeDto>> CreateAttribute(
        int id,
        [FromBody] CreateProductAttributeDto dto)
    {
        dto.ProductId = id;
        var created = await _productService.CreateAttributeAsync(dto);
        return CreatedAtAction(nameof(GetAttributes), new { id }, created);
    }

    /// <summary>ویرایش واریانت محصول.</summary>
    [HttpPut("{id:int}/variants/{variantId:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVariant(
        int id,
        int variantId,
        [FromBody] UpdateProductVariantDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _productService.UpdateVariantAsync(variantId, dto, cancellationToken);
        if (updated is null)
            return NotFound();

        return NoContent();
    }

    /// <summary>حذف واریانت محصول.</summary>
    [HttpDelete("{id:int}/variants/{variantId:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVariant(
        int id,
        int variantId,
        CancellationToken cancellationToken)
    {
        var success = await _productService.DeleteVariantAsync(variantId, cancellationToken);
        if (!success)
            return NotFound();

        return NoContent();
    }
}