namespace Application.Options
{
    /// <summary>Phase 5 — تنظیمات ذخیره‌سازی فایل.</summary>
    public class StorageSettings
    {
        public const string SectionName = "StorageSettings";

        /// <summary>پوشهٔ ریشه روی دیسک.</summary>
        public string RootPath { get; set; } = "wwwroot/uploads";

        /// <summary>پیشوند URL عمومی که به مسیر فایل چسبانده می‌شود.</summary>
        public string PublicBaseUrl { get; set; } = "/uploads";

        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

        public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];
    }
}