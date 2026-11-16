using Asp.Versioning;
using BusinessLogic.DTOs.Media;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Validators.Common;
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
        [FromForm] UploadProductImageForm form,
        CancellationToken cancellationToken)
    {
        var file = form.File;
        var alt = form.Alt;
        var isPrimary = form.IsPrimary;

        if (file is null || file.Length == 0)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "فایلی ارسال نشده است.",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = "FILE_REQUIRED";
            return BadRequest(problem);
        }

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

public class AddProductImageUrlRequest : System.ComponentModel.DataAnnotations.IValidatableObject
{
    // Batch 48 (SEC-12): only absolute https URLs; the model-state pipeline turns this into 422 VALIDATION_ERROR.
    public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(
        System.ComponentModel.DataAnnotations.ValidationContext validationContext)
    {
        if (!HttpsUrl.IsValid(Url))
            yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                "آدرس تصویر باید یک آدرس مطلق با پروتکل https باشد.", new[] { nameof(Url) });
        else if (Url.Length > 500)
            yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                "آدرس تصویر نباید بیش از ۵۰۰ کاراکتر باشد.", new[] { nameof(Url) });
    }

    public string Url { get; set; } = null!;
    public string? Alt { get; set; }
    public bool IsPrimary { get; set; }
}
/// <summary>
/// Batch 67 (API-05): فیلدهای فرم آپلود تصویر. Swashbuckle ترکیب <c>IFormFile</c> با چند پارامتر
/// <c>[FromForm]</c> جدا را نمی‌پذیرد و تولید سند OpenAPI را می‌شکست؛ نام فیلدهای فرم (file، alt، isPrimary) تغییر نکرده‌اند.
/// <c>File</c> عمداً nullable است تا نبود فایل همچنان پاسخ <c>FILE_REQUIRED</c> بدهد.
/// </summary>
public sealed class UploadProductImageForm
{
    public IFormFile? File { get; set; }
    public string? Alt { get; set; }
    public bool IsPrimary { get; set; }
}
