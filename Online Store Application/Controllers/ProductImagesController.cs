using Asp.Versioning;
using BusinessLogic.DTOs.Media;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>Phase 5 — گالری تصاویر محصول.</summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/products/{productId:int}/images")]
public class ProductImagesController : ControllerBase
{
    private readonly IProductImageService _imageService;

    public ProductImagesController(IProductImageService imageService)
    {
        _imageService = imageService;
    }

    /// <summary>دریافت فهرست تصاویر یک محصول.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<ProductImageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductImageDto>>> Get(int productId, CancellationToken cancellationToken)
        => Ok(await _imageService.GetForProductAsync(productId, cancellationToken));

    /// <summary>
    /// آپلود تصویر. فرمت از kind فایل تعیین می‌شود، نه از ادعای کلاینت.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductImageDto>> Upload(
        int productId,
        [FromForm] IFormFile file,
        [FromForm] string? alt,
        [FromForm] bool isPrimary,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشده است.", code = "FILE_REQUIRED" });

        await using var stream = file.OpenReadStream();

        var result = await _imageService.UploadAsync(
            productId,
            stream,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            alt,
            isPrimary,
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { productId }, result);
    }

    /// <summary>ثبت تصویر محصول از طریق آدرس اینترنتی (URL).</summary>
    [HttpPost("url")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductImageDto>> AddByUrl(
        int productId,
        [FromBody] AddProductImageUrlRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _imageService.AddAsync(
            productId,
            request.Url,
            request.Alt,
            request.IsPrimary,
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { productId }, result);
    }

    /// <summary>حذف تصویر محصول.</summary>
    [HttpDelete("{imageId:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int productId, int imageId, CancellationToken cancellationToken)
    {
        await _imageService.DeleteAsync(productId, imageId, cancellationToken);
        return NoContent();
    }
}

public class AddProductImageUrlRequest
{
    public string Url { get; set; } = null!;
    public string? Alt { get; set; }
    public bool IsPrimary { get; set; }
}