using System.Diagnostics;
using Application.Interfaces;
using Application.Models.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Application.Middleware;

public class QueryMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<QueryMetricsMiddleware> _logger;
    private readonly IQueryMetricsService _metricsService;

    public QueryMetricsMiddleware(
        RequestDelegate next,
        ILogger<QueryMetricsMiddleware> logger,
        IQueryMetricsService metricsService)
    {
        _next = next;
        _logger = logger;
        _metricsService = metricsService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceIdentifier,
            ["Path"] = context.Request.Path,
            ["Method"] = context.Request.Method
        }))
        {
            var sw = Stopwatch.StartNew();
            var filter = context.Request.Query["filter"].FirstOrDefault();
            var sort = context.Request.Query["sort"].FirstOrDefault();
            bool exceptionThrown = false;

            try
            {
                await _next(context);
            }
            catch
            {
                exceptionThrown = true;
                throw;
            }
            finally
            {
                sw.Stop();

                string? userId = null;
                string? userName = null;
                try
                {
                    var userService = context.RequestServices.GetService(typeof(ICurrentUserService)) as ICurrentUserService;
                    if (userService != null)
                    {
                        var userIdValue = userService.TryGetCurrentUserId();
                        userId = userIdValue?.ToString();
                        userName = userService.GetCurrentUserName();
                    }
                }
                catch { }

                var metrics = new QueryMetrics
                {
                    Path = context.Request.Path,
                    Filter = filter,
                    Sort = sort,
                    FilterLength = filter?.Length ?? 0,
                    SortFields = sort?.Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
                    FilterConditions = CountConditions(filter),
                    ElapsedMilliseconds = sw.ElapsedMilliseconds,
                    HasException = exceptionThrown,
                    UserId = userId,
                    UserName = userName,
                    TraceId = context.TraceIdentifier 
                };

                await _metricsService.LogAsync(metrics);
            }
        }
    }

    private int CountConditions(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return 0;
        return filter.Split(new[] { " and ", " or " }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}