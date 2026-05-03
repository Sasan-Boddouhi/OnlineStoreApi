using Application.Models.Metrics;

namespace Application.Interfaces;

public interface IQueryMetricsService
{
    /// <summary>ثبت متریک‌های یک کوئری (مثلاً از Middleware یا سرویس‌های داخلی).</summary>
    Task LogAsync(QueryMetrics metrics);
}