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
            .Configure(options =>
            {
                var section = configuration.GetSection(BackgroundJobOptions.SectionName);

                options.QueueCapacity = ReadInt(section["QueueCapacity"], options.QueueCapacity);
                options.EnqueueTimeoutSeconds = ReadInt(section["EnqueueTimeoutSeconds"], options.EnqueueTimeoutSeconds);
                options.MaxIdempotencyKeyLength = ReadInt(section["MaxIdempotencyKeyLength"], options.MaxIdempotencyKeyLength);
                options.RetryQueueCapacity = ReadInt(section["RetryQueueCapacity"], options.RetryQueueCapacity);
                options.RetryEnqueueTimeoutSeconds = ReadInt(section["RetryEnqueueTimeoutSeconds"], options.RetryEnqueueTimeoutSeconds);
                options.RetryBaseDelaySeconds = ReadInt(section["RetryBaseDelaySeconds"], options.RetryBaseDelaySeconds);
                options.RetryMaxDelaySeconds = ReadInt(section["RetryMaxDelaySeconds"], options.RetryMaxDelaySeconds);
                options.MaxExecutionAttempts = ReadInt(section["MaxExecutionAttempts"], options.MaxExecutionAttempts);
                options.MaxRequeueAttempts = ReadInt(section["MaxRequeueAttempts"], options.MaxRequeueAttempts);
                options.JobTimeoutSeconds = ReadInt(section["JobTimeoutSeconds"], options.JobTimeoutSeconds);
            })
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

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
