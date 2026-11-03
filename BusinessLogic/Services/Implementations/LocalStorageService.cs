using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations
{
    /// <summary>
    /// Phase 5 — ذخیره‌سازی روی دیسک محلی.
    ///
    /// نام فایل ورودی استفاده نمی‌شود (خطر path traversal)؛ فقط پسوند آن پس از
    /// اعتبارسنجی در برابر فهرست مجاز نگه داشته می‌شود و نام نهایی تصادفی است.
    /// </summary>
    public sealed class LocalStorageService : IStorageService
    {
        private readonly ILogger<LocalStorageService> _logger;
        private readonly StorageSettings _settings;
        private readonly string _rootPath;

        public LocalStorageService(IOptions<StorageSettings> settings, ILogger<LocalStorageService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            _rootPath = Path.IsPathRooted(_settings.RootPath)
                ? _settings.RootPath
                : Path.Combine(Directory.GetCurrentDirectory(), _settings.RootPath);

            Directory.CreateDirectory(_rootPath);
        }

        public async Task<string> SaveAsync(
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (content is null || !content.CanRead)
                throw new BusinessException("فایل ارسالی خوانا نیست.", "INVALID_FILE");

            var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

            if (!_settings.AllowedExtensions.Contains(extension))
                throw new BusinessException(
                    $"فرمت فایل مجاز نیست. مجاز: {string.Join(", ", _settings.AllowedExtensions)}",
                    "INVALID_FILE_TYPE");

            if (content.CanSeek && content.Length > _settings.MaxFileSizeBytes)
                throw new BusinessException(
                    $"حجم فایل حداکثر {_settings.MaxFileSizeBytes / 1024 / 1024} مگابایت است.",
                    "FILE_TOO_LARGE");

            // M7.12: Magic byte validation against executable/malicious uploads disguised as images
            var header = new byte[12];
            var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (!ValidateImageSignature(header, extension))
                throw new BusinessException("محتوای فایل با پسوند تصویر هم‌خوانی ندارد.", "INVALID_FILE_CONTENT");

            if (content.CanSeek)
            {
                content.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                var memoryStream = new MemoryStream();
                await memoryStream.WriteAsync(header.AsMemory(0, bytesRead), cancellationToken);
                await content.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Seek(0, SeekOrigin.Begin);
                content = memoryStream;
            }

            var safeName = $"{Guid.NewGuid():N}{extension}";
            var targetPath = Path.Combine(_rootPath, safeName);

            await using (var fileStream = File.Create(targetPath))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            _logger.LogInformation("File stored as {FileName} ({ContentType})", safeName, contentType);

            return $"{_settings.PublicBaseUrl.TrimEnd('/')}/{safeName}";
        }

        private static bool ValidateImageSignature(byte[] header, string extension)
        {
            if (header.Length < 4)
                return false;

            return extension switch
            {
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                ".webp" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46,
                _ => false
            };
        }

        public Task DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Task.CompletedTask;

            // فقط بخش نام فایل اهمیت دارد؛ هر چیز دیگری دور ریخته می‌شود.
            var fileName = Path.GetFileName(url);
            if (string.IsNullOrWhiteSpace(fileName))
                return Task.CompletedTask;

            var targetPath = Path.Combine(_rootPath, fileName);

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                _logger.LogInformation("File {FileName} deleted", fileName);
            }

            return Task.CompletedTask;
        }
    }
}