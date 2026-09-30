using Application.BackgroundJobs;
using FluentAssertions;
using Infrastructure.BackgroundJobs.HealthChecks;
using Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.HealthChecks;

public sealed class HealthEndpointsTests : IClassFixture<IntegrationTestFactory<Program>>
{
    private readonly IntegrationTestFactory<Program> _factory;

    public HealthEndpointsTests(IntegrationTestFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task LiveEndpoint_AlwaysReturns200()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyEndpoint_HealthyState_Returns200()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_AfterQueueShutdown_ReportsUnhealthy()
    {
        using var factory = CreateFactory();
        _ = factory.CreateClient();

        var healthService = factory.Services.GetRequiredService<HealthCheckService>();
        var queue = factory.Services.GetRequiredService<ChannelBackgroundJobQueue>();

        var beforeResult = await healthService.CheckHealthAsync(
            result => result.Tags.Contains("ready"));
        beforeResult.Status.Should().Be(HealthStatus.Healthy);

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();

        var shutdownDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!queue.IsShuttingDown && DateTime.UtcNow < shutdownDeadline)
            await Task.Delay(25);

        queue.IsShuttingDown.Should().BeTrue();

        var afterResult = await healthService.CheckHealthAsync(
            result => result.Tags.Contains("ready"));
        afterResult.Status.Should().Be(HealthStatus.Unhealthy);
    }

    private WebApplicationFactory<Program> CreateFactory()
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddBackgroundProcessing(
                new ConfigurationBuilder().AddInMemoryCollection().Build());
            services.AddBackgroundJobHandler<HealthCheckJob, HealthCheckJobHandler>();
        }));

    private sealed record HealthCheckJob;

    private sealed class HealthCheckJobHandler : IBackgroundJobHandler<HealthCheckJob>
    {
        public Task HandleAsync(
            HealthCheckJob job,
            BackgroundJobExecutionContext context,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
