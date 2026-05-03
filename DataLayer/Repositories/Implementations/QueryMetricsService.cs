using Application.Interfaces;
using Application.Models.Metrics;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public class QueryMetricsService : IQueryMetricsService
{
    private readonly ILogger<QueryMetricsService> _logger;

    public QueryMetricsService(ILogger<QueryMetricsService> logger) => _logger = logger;

    public Task LogAsync(QueryMetrics metrics)
    {
        // ثبت ساختاریافته برای جستجوی بهتر در سیستم لاگ
        _logger.LogInformation(
            "QueryMetrics | TraceId: {TraceId} | Path: {Path} | UserId: {UserId} | " +
            "Time: {Time}ms | FilterLen: {FilterLen} | SortFields: {SortFields} | " +
            "Conditions: {Conditions} | Exception: {Exception}",
            metrics.TraceId,
            metrics.Path,
            metrics.UserId,
            metrics.ElapsedMilliseconds,
            metrics.FilterLength,
            metrics.SortFields,
            metrics.FilterConditions,
            metrics.HasException);

        return Task.CompletedTask;
    }
}