using Application.Options;

namespace Online_Store_Application.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddRedis(this IServiceCollection services, RedisOptions redisOptions)
    {
        services.AddOptions<RedisOptions>()
            .BindConfiguration(RedisOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisOptions.Configuration;
            options.InstanceName = redisOptions.InstanceName;
        });

        return services;
    }
}