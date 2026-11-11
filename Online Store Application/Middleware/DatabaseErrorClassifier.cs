using Microsoft.EntityFrameworkCore;

namespace Online_Store_Application.Middleware;

/// <summary>
/// Batch 7 (F-16) — تشخیص نقض ایندکس/کلید یکتا از روی استثنای ارائه‌دهندهٔ دیتابیس.
///
/// پروژهٔ API به بستهٔ SQLite وابسته نیست (SQLite فقط در تست‌ها استفاده می‌شود) و نباید باشد؛
/// پس نوع استثنا با نام و ویژگی‌هایش شناخته می‌شود:
///  • SQL Server: SqlException.Number = 2601 (ایندکس یکتا) یا 2627 (کلید یکتا/اصلی)
///  • SQLite:     SqliteException.SqliteExtendedErrorCode = 2067 (UNIQUE) یا 1555 (PRIMARY KEY)
/// خطاهای دیگر دیتابیس (قطع اتصال، پر شدن دیسک، ...) عمداً 500 می‌مانند.
///
/// Batch 7b: استثنای ارائه‌دهنده معمولاً داخل DbUpdateException پیچیده شده است، ولی مسیرهایی مثل
/// ExecuteUpdate/ExecuteSql آن را مستقیم پرتاب می‌کنند؛ پس خود استثنا و کل زنجیرهٔ Inner بررسی می‌شود.
/// </summary>
internal static class DatabaseErrorClassifier
{
    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current.GetType().Name)
            {
                case "SqlException" when ReadInt(current, "Number") is 2601 or 2627:
                case "SqliteException" when ReadInt(current, "SqliteExtendedErrorCode") is 2067 or 1555:
                    return true;
            }
        }

        return false;
    }

    private static int? ReadInt(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName)?.GetValue(instance) as int?;
}
