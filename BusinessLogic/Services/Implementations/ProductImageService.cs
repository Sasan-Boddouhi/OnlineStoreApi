using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Media;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations
{
    /// <summary>
    /// Phase 5 — گالری تصاویر محصول.
    /// هر محصول همیشه حداکثر یک تصویر شاخص دارد؛ این را در کد تضمین می‌کنیم
    /// و دیتابیس با ایندکس یکتای فیلترشده پشتیبان آن است.
    /// </summary>
    public sealed class ProductImageService : IProductImageService
    {
        private const int MaxImagesPerProduct = 10;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IStorageService _storageService;
        private readonly ILogger<ProductImageService> _logger;
        private readonly StorageSettings _storageSettings;
        private readonly ProductImageUrlOptions _urlOptions;

        public ProductImageService(
            IUnitOfWork unitOfWork,
            IStorageService storageService,
            IOptions<StorageSettings> storageSettings,
            IOptions<ProductImageUrlOptions> urlOptions,
            ILogger<ProductImageService> logger)
        {
            _unitOfWork = unitOfWork;
            _storageService = storageService;
            _storageSettings = storageSettings.Value;
            _urlOptions = urlOptions.Value;
            _logger = logger;
        }

        public async Task<ProductImageDto> UploadAsync(
            int productId,
            Stream content,
            string fileName,
            string contentType,
            string? alt,
            bool isPrimary,
            CancellationToken cancellationToken = default)
        {
            await EnsureProductExistsAsync(productId, cancellationToken);

            if (content.CanSeek && content.Length > _storageSettings.MaxFileSizeBytes)
                throw new BusinessException(
                    $"حجم فایل حداکثر {_storageSettings.MaxFileSizeBytes / 1024 / 1024} مگابایت است.",
                    "FILE_TOO_LARGE");

            var url = await _storageService.SaveAsync(content, fileName, contentType, cancellationToken);

            try
            {
                return await RegisterAsync(productId, url, alt, isPrimary, cancellationToken);
            }
            catch
            {
                // اگر ثبت در گالری شکست خورد، فایل یتیم روی دیسک نمی‌گذاریم.
                await _storageService.DeleteAsync(url, cancellationToken);
                throw;
            }
        }

        public async Task<ProductImageDto> AddAsync(
            int productId,
            string url,
            string? alt,
            bool isPrimary,
            CancellationToken cancellationToken = default)
        {
            await EnsureProductExistsAsync(productId, cancellationToken);

            if (string.IsNullOrWhiteSpace(url))
                throw new BusinessException("آدرس تصویر الزامی است.", "IMAGE_URL_REQUIRED");

            // Batch 48c: مسیر «ثبت با آدرس» فقط آدرس خارجی مجاز را می‌پذیرد؛ آدرس محلی آپلود از RegisterAsync می‌گذرد.
            EnsureExternalUrlAllowed(url.Trim());

            return await RegisterAsync(productId, url, alt, isPrimary, cancellationToken);
        }

        /// <summary>
        /// آدرس باید مطلق و https باشد، user-info و پورت غیرپیش‌فرض نداشته باشد و میزبانش (IdnHost، بدون حساسیت به حروف،
        /// مطابقت دقیق) در <see cref="ProductImageUrlOptions.AllowedExternalHosts"/> باشد.
        /// </summary>
        private void EnsureExternalUrlAllowed(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrEmpty(uri.Host))
                throw new UnprocessableEntityException(
                    "آدرس تصویر باید یک آدرس مطلق با پروتکل https باشد.", "VALIDATION_ERROR");

            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new UnprocessableEntityException(
                    "آدرس تصویر نباید شامل نام کاربری یا گذرواژه باشد.", "IMAGE_HOST_NOT_ALLOWED");

            if (!uri.IsDefaultPort)
                throw new UnprocessableEntityException(
                    "آدرس تصویر باید از پورت پیش‌فرض https استفاده کند.", "IMAGE_HOST_NOT_ALLOWED");

            var allowed = (_urlOptions.AllowedExternalHosts ?? [])
                .Where(ProductImageUrlOptions.IsValidHost)
                .Select(h => new Uri("https://" + h).IdnHost);

            if (!allowed.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
                throw new UnprocessableEntityException(
                    "میزبان این آدرس تصویر مجاز نیست.", "IMAGE_HOST_NOT_ALLOWED");
        }

        /// <summary>آدرس محلی تولیدشده توسط همین سرور: <c>{PublicBaseUrl}/{نام‌فایل}</c> بدون هیچ زیرمسیر.</summary>
        private bool IsLocalUploadUrl(string url)
        {
            var prefix = _storageSettings.PublicBaseUrl.TrimEnd('/') + "/";
            if (!url.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var name = url[prefix.Length..];
            return name.Length > 0 && name.IndexOfAny(['/', '\\', '?', '#', ':']) < 0 && name != "." && name != "..";
        }

        private async Task<ProductImageDto> RegisterAsync(
            int productId,
            string url,
            string? alt,
            bool isPrimary,
            CancellationToken cancellationToken)
        {
            var existing = await LoadImagesAsync(productId, tracking: true, cancellationToken);

            if (existing.Count >= MaxImagesPerProduct)
                throw new BusinessException(
                    $"حداکثر {MaxImagesPerProduct} تصویر برای هر محصول مجاز است.",
                    "IMAGE_LIMIT_EXCEEDED");

            // اولین تصویر خودکار شاخص می‌شود تا محصول بدون تصویر شاخص نماند.
            var makePrimary = isPrimary || existing.Count == 0;

            if (makePrimary)
                ClearPrimaryFlags(existing);

            var image = new ProductImage
            {
                ProductId = productId,
                Url = url.Trim(),
                Alt = alt,
                SortOrder = existing.Count == 0 ? 0 : existing.Max(i => i.SortOrder) + 1,
                IsPrimary = makePrimary
            };

            await _unitOfWork.Repository<ProductImage>().AddAsync(image, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Image {ImageId} added to product {ProductId}", image.ProductImageId, productId);

            return Map(image);
        }

        public async Task DeleteAsync(int productId, int imageId, CancellationToken cancellationToken = default)
        {
            var images = await LoadImagesAsync(productId, tracking: true, cancellationToken);
            var image = images.FirstOrDefault(i => i.ProductImageId == imageId);

            if (image is null)
                throw new NotFoundException("تصویر یافت نشد.", "IMAGE_NOT_FOUND");

            var wasPrimary = image.IsPrimary;

            _unitOfWork.Repository<ProductImage>().Delete(image);
            images.Remove(image);

            // اگر تصویر شاخص حذف شد، قدیمی‌ترین تصویر باقی‌مانده شاخص می‌شود.
            if (wasPrimary && images.Count > 0)
            {
                var next = images.OrderBy(i => i.SortOrder).ThenBy(i => i.ProductImageId).First();
                next.IsPrimary = true;
                _unitOfWork.Repository<ProductImage>().Update(next);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // Batch 48c: StorageService نام فایل را از آدرس می‌گیرد؛ آدرس خارجی با همان نام فایل نباید فایل آپلودشده را پاک کند.
            if (IsLocalUploadUrl(image.Url))
                await _storageService.DeleteAsync(image.Url, cancellationToken);

            _logger.LogInformation("Image {ImageId} removed from product {ProductId}", imageId, productId);
        }

        public async Task<IEnumerable<ProductImageDto>> GetForProductAsync(
            int productId,
            CancellationToken cancellationToken = default)
        {
            var images = await LoadImagesAsync(productId, tracking: false, cancellationToken);
            return images.Select(Map);
        }

        private async Task EnsureProductExistsAsync(int productId, CancellationToken cancellationToken)
        {
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId, cancellationToken);
            if (product is null)
                throw new NotFoundException("محصول یافت نشد.", "PRODUCT_NOT_FOUND");
        }

        private async Task<List<ProductImage>> LoadImagesAsync(
            int productId,
            bool tracking,
            CancellationToken cancellationToken)
        {
            var spec = new Spec<ProductImage>()
                .Where(i => i.ProductId == productId)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.ProductImageId);

            if (tracking)
                spec.AsTracking();

            var images = await _unitOfWork.Repository<ProductImage>().ListAsync(spec, cancellationToken);
            return images.ToList();
        }

        private void ClearPrimaryFlags(IEnumerable<ProductImage> images)
        {
            foreach (var image in images.Where(i => i.IsPrimary))
            {
                image.IsPrimary = false;
                _unitOfWork.Repository<ProductImage>().Update(image);
            }
        }

        private static ProductImageDto Map(ProductImage image) => new()
        {
            ProductImageId = image.ProductImageId,
            ProductId = image.ProductId,
            Url = image.Url,
            Alt = image.Alt,
            SortOrder = image.SortOrder,
            IsPrimary = image.IsPrimary
        };
    }
}