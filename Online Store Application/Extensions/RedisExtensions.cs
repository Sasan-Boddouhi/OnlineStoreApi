using Application.Options;
using StackExchange.Redis;

namespace Online_Store_Application.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddRedis(this IServiceCollection services, RedisOptions redisOptions)
    {
        services.AddOptions<RedisOptions>()
            .BindConfiguration(RedisOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var configOptions = ConfigurationOptions.Parse(redisOptions.Configuration);
        configOptions.AbortOnConnectFail = false;
        configOptions.ClientName = "OnlineStoreApi";

        var multiplexer = ConnectionMultiplexer.Connect(configOptions);

        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisOptions.Configuration;
            options.InstanceName = redisOptions.InstanceName;
            options.ConnectionMultiplexerFactory =
                () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
        });

        return services;
    }
}
