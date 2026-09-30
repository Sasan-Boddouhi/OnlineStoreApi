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
    public async Task QueueHealthCheck_WhenShuttingDown_ReturnsUnhealthy()
    {
        var lifetime = new TestHostApplicationLifetime();
        var queue = new ChannelBackgroundJobQueue(
            Options.Create(new ChannelBackgroundJobQueueOptions
            {
                QueueCapacity = 100,
                EnqueueTimeoutSeconds = 1,
                MaxIdempotencyKeyLength = 256
            }),
            lifetime,
            TimeProvider.System);

        var check = new BackgroundJobQueueHealthCheck(queue);

        lifetime.Stop();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    private WebApplicationFactory<Program> CreateFactory()
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddBackgroundProcessing(
                new ConfigurationBuilder().AddInMemoryCollection().Build());
            services.AddBackgroundJobHandler<HealthCheckJob, HealthCheckJobHandler>();
        }));

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Stop() => _stopping.Cancel();
        public void StopApplication() => _stopping.Cancel();
    }

    private sealed record HealthCheckJob;

    private sealed class HealthCheckJobHandler : IBackgroundJobHandler<HealthCheckJob>
    {
        public Task HandleAsync(
            HealthCheckJob job,
            BackgroundJobExecutionContext context,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
