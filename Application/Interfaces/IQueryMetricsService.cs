using Application.Models.Metrics;

namespace Application.Interfaces;

public interface IQueryMetricsService
{
    Task LogAsync(QueryMetrics metrics);
}