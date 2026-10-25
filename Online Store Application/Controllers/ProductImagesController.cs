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

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ProductImageDto>>> Get(int productId, CancellationToken cancellationToken)
        => Ok(await _imageService.GetForProductAsync(productId, cancellationToken));

    /// <summary>
    /// آپلود تصویر. فرمت از kind فایل تعیین می‌شود، نه از ادعای کلاینت.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageCatalog")]
    [RequestSizeLimit(10 * 1024 * 1024)]
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

        return Ok(result);
    }

    [HttpDelete("{imageId:int}")]
    [Authorize(Policy = "CanManageCatalog")]
    public async Task<IActionResult> Delete(int productId, int imageId, CancellationToken cancellationToken)
    {
        await _imageService.DeleteAsync(productId, imageId, cancellationToken);
        return NoContent();
    }
}