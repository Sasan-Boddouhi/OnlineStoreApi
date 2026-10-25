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
}