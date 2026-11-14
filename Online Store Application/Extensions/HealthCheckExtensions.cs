using Application.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Online_Store_Application.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddApplicationHealthChecks(
        this IServiceCollection services,
        DatabaseOptions databaseOptions,
        RedisOptions redisOptions)
    {
        redisOptions ??= new RedisOptions
        {
            Configuration = "localhost:6379",
            InstanceName = "OnlineStore_Test:"
        };

        services.AddHealthChecks()
            .AddSqlServer(
                databaseOptions.DefaultConnection,
                name: "sqlserver",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "db", "sql", "sqlserver" })
            .AddRedis(
                redisOptions.Configuration,
                name: "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "cache", "redis" });

        return services;
    }

    /// <summary>
    /// Batch 52 (OPS-07): سیاست مجوز خروجی تفصیلی <c>/health</c> بیرون از Development.
    /// </summary>
    public const string DetailedHealthPolicy = "AdminOnly";

    /// <summary>
    /// Batch 52 (OPS-07): <c>/health</c> نام وابستگی‌ها، مدت و توضیح خطا را برمی‌گرداند؛
    /// بیرون از Development فقط مدیر (سیاست <see cref="DetailedHealthPolicy"/>) آن را می‌بیند.
    /// در Development برای راحتی توسعه ناشناس می‌ماند. <c>/health/live</c> و <c>/health/ready</c>
    /// جدا نگاشت می‌شوند و همیشه ناشناس و حداقلی‌اند (فقط Healthy/Unhealthy).
    /// </summary>
    public static TBuilder RequireDetailedHealthAccess<TBuilder>(
        this TBuilder builder,
        IHostEnvironment environment)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsDevelopment()
            ? builder.AllowAnonymous()
            : builder.RequireAuthorization(DetailedHealthPolicy);
    }
}