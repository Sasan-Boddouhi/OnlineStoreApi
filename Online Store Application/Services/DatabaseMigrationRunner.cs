using DataLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace Online_Store_Application.Services;

/// <summary>
/// Staging: اجرای خودکار مایگریشن‌ها هنگام بالا آمدن.
///
/// فقط با <c>Database:AutoMigrate = true</c> فعال می‌شود. برای production
/// عمداً خاموش می‌ماند: deploy نباید اسکیمای production را بدون بازبینی عوض کند.
///
/// retry لازم است چون کانتینر SQL Server معمولاً چند ثانیه دیرتر از API
/// آماده می‌شود و "database is not available" اولین تلاش را می‌کشد.
/// </summary>
public static class DatabaseMigrationRunner
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:AutoMigrate"))
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        logger?.LogInformation(
            "Auto-migrate enabled. {Count} pending migration(s): {Migrations}",
            pending.Count,
            pending.Count == 0 ? "(none)" : string.Join(", ", pending));

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);

                logger?.LogInformation("Database migrations applied successfully on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger?.LogWarning(
                    ex,
                    "Migration attempt {Attempt}/{MaxAttempts} failed. Retrying in {Delay}…",
                    attempt, MaxAttempts, Delay);

                await Task.Delay(Delay, cancellationToken);
            }
        }

        // آخرین تلاش بدون catch: اگر اینجا شکست بخورد، API نباید
        // با اسکیمای قدیمی به درخواست‌ها جواب بدهد.
        await db.Database.MigrateAsync(cancellationToken);
        logger?.LogInformation("Database migrations applied successfully on final attempt.");
    }
}