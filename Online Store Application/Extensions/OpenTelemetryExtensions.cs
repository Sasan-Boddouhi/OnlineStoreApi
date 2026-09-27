using Application.Diagnostics;
using Application.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Online_Store_Application.Extensions;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<OtelOptions>()
            .Bind(configuration.GetSection(OtelOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddOpenTelemetryServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(OtelOptions.SectionName)
            .Get<OtelOptions>() ?? new OtelOptions();

        var enabled = options.Enabled
            && !environment.IsEnvironment("Testing");

        if (!enabled)
        {
            return services;
        }

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName,
                    ["host.name"] = Environment.MachineName
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(options.TraceSamplingRatio)))
                    .AddAspNetCoreInstrumentation(aspnet =>
                    {
                        aspnet.RecordException = true;
                        aspnet.Filter = context =>
                        {
                            var path = context.Request.Path.Value ?? string.Empty;

                            return !path.StartsWith(
                                "/health",
                                StringComparison.OrdinalIgnoreCase);
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("OnlineStore.Auth")
                    .AddSource("OnlineStore.Services")
                    .AddSource("OnlineStore.Cache");

                if (options.IncludeEfCore)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation();
                }

                if (options.IncludeRedis)
                {
                    tracing.AddRedisInstrumentation(redisOptions =>
                    {
                        redisOptions.SetVerboseDatabaseStatements = false;
                    });
                }

                tracing.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(options.Endpoint);
                        otlp.TimeoutMilliseconds = options.ExportTimeoutMs;
                        otlp.Protocol = OtlpExportProtocol.Grpc;
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(OnlineStoreMetrics.AuthMeterName)
                    .AddMeter(OnlineStoreMetrics.CacheMeterName)
                    .AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(options.Endpoint);
                        otlp.TimeoutMilliseconds = options.ExportTimeoutMs;
                        otlp.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

        return services;
    }
}
