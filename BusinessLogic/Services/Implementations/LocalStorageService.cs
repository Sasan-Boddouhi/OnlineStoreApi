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

            var safeName = $"{Guid.NewGuid():N}{extension}";
            var targetPath = Path.Combine(_rootPath, safeName);

            await using (var fileStream = File.Create(targetPath))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            _logger.LogInformation("File stored as {FileName} ({ContentType})", safeName, contentType);

            return $"{_settings.PublicBaseUrl.TrimEnd('/')}/{safeName}";
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