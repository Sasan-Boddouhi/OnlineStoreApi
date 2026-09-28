using Application.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

public static class BackgroundJobServiceCollectionExtensions
{
    public static IServiceCollection AddBackgroundProcessing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BackgroundJobOptions>()
            .Bind(configuration.GetSection(BackgroundJobOptions.SectionName))
            .Validate(options =>
            {
                options.Validate();
                return true;
            }, "BackgroundJobs configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<ChannelBackgroundJobQueueOptions>()
            .Configure<IOptions<BackgroundJobOptions>>(
                (queueOptions, backgroundOptions) =>
                {
                    queueOptions.QueueCapacity = backgroundOptions.Value.QueueCapacity;
                    queueOptions.EnqueueTimeoutSeconds = backgroundOptions.Value.EnqueueTimeoutSeconds;
                    queueOptions.MaxIdempotencyKeyLength = backgroundOptions.Value.MaxIdempotencyKeyLength;
                })
            .Validate(options =>
            {
                options.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ChannelBackgroundJobQueue>();
        services.AddSingleton<IBackgroundJobQueue>(
            sp => sp.GetRequiredService<ChannelBackgroundJobQueue>());
        services.AddSingleton<IBackgroundJobConsumer>(
            sp => sp.GetRequiredService<ChannelBackgroundJobQueue>());

        services.AddSingleton<IRetryPolicy, StaticRetryPolicy>();
        services.AddSingleton<RetryScheduler>();
        services.AddSingleton<BackgroundJobDispatcher>();

        services.AddHostedService<BackgroundJobRegistrationValidator>();
        services.AddHostedService(sp => sp.GetRequiredService<RetryScheduler>());
        services.AddHostedService<BackgroundJobWorker>();

        return services;
    }

    public static IServiceCollection AddBackgroundJobHandler<TJob, THandler>(
        this IServiceCollection services)
        where THandler : class, IBackgroundJobHandler<TJob>
    {
        services.AddScoped<IBackgroundJobHandler<TJob>, THandler>();
        services.AddSingleton(
            new BackgroundJobHandlerRegistration(
                typeof(TJob),
                typeof(THandler),
                typeof(TJob).FullName ?? typeof(TJob).Name));

        return services;
    }
}
