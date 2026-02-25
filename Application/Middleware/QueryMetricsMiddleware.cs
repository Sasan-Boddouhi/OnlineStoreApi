using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;
using Application.Interfaces;
using Application.Models.Metrics;

namespace Application.Middleware
{
    public class QueryMetricsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<QueryMetricsMiddleware> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor; // برای دسترسی به HttpContext در متدهای غیر همزمان

        public QueryMetricsMiddleware(
            RequestDelegate next,
            ILogger<QueryMetricsMiddleware> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _next = next;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task InvokeAsync(HttpContext context)
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

                // دریافت اطلاعات کاربر از طریق سرویس‌های اسکوپ‌شده
                var userService = context.RequestServices.GetService(typeof(ICurrentUserService)) as ICurrentUserService;
                string? userId = userService?.GetCurrentUserId().ToString();
                string? userName = userService?.GetCurrentUserName();

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
                    UserName = userName
                };

                LogMetrics(metrics);
            }
        }

        private int CountConditions(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return 0;

            // تخمین تعداد شرایط با شمارش عملگرهای and و or
            return filter.Split(new[] { " and ", " or " }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private void LogMetrics(QueryMetrics metrics)
        {
            // ثبت ساختاریافته برای قابلیت جستجوی بهتر
            _logger.LogInformation(
                "QueryMetrics | Path: {Path} | UserId: {UserId} | UserName: {UserName} | Time: {Time}ms | FilterLen: {FilterLen} | SortFields: {SortFields} | Conditions: {Conditions} | Exception: {Exception}",
                metrics.Path,
                metrics.UserId,
                metrics.UserName,
                metrics.ElapsedMilliseconds,
                metrics.FilterLength,
                metrics.SortFields,
                metrics.FilterConditions,
                metrics.HasException
            );
        }
    }
}