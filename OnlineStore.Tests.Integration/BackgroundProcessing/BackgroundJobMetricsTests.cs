using System.Diagnostics.Metrics;
using Application.BackgroundJobs;
using Application.Diagnostics;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.BackgroundProcessing;

public sealed class BackgroundJobMetricsTests : IClassFixture<IntegrationTestFactory<Program>>
{
    private readonly IntegrationTestFactory<Program> _factory;

    public BackgroundJobMetricsTests(IntegrationTestFactory<Program> factory)
        => _factory = factory;

    [Fact]
    public async Task EnqueueAndComplete_EmitsEnqueuedAndCompletedCounters()
    {
        // Arrange
        var targetJobType = typeof(BackgroundJobMetricsTestJob).FullName!;
        var capture = new MetricsCapture();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OnlineStoreMetrics.BackgroundJobsMeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? jobType = null;
            foreach (var tag in tags)
                if (tag.Key == "job.type")
                    jobType = tag.Value?.ToString();

            if (jobType != targetJobType) return;

            if (instrument.Name == "background_jobs.enqueued")
                Interlocked.Add(ref capture.EnqueuedCount, value);
            else if (instrument.Name == "background_jobs.completed")
                Interlocked.Add(ref capture.CompletedCount, value);
        });
        listener.Start();

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddBackgroundProcessing(
                    new ConfigurationBuilder().AddInMemoryCollection().Build());
                services.AddBackgroundJobHandler<BackgroundJobMetricsTestJob, RecordingHandler>();
                services.AddSingleton<MetricsTestState>();
            });
        });

        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var state = factory.Services.GetRequiredService<MetricsTestState>();

        // Act
        var result = await queue.EnqueueAsync(
            new BackgroundJobMetricsTestJob(42),
            $"metrics-test-{Guid.NewGuid()}",
            CancellationToken.None);

        result.IsAccepted.Should().BeTrue();
        await state.Handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        // Assert
        capture.EnqueuedCount.Should().BeGreaterThanOrEqualTo(1);
        capture.CompletedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    public sealed record BackgroundJobMetricsTestJob(int Value);

    public sealed class MetricsTestState
    {
        public TaskCompletionSource<object?> Handled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class MetricsCapture
    {
        public long EnqueuedCount;
        public long CompletedCount;
    }

    private sealed class RecordingHandler : IBackgroundJobHandler<BackgroundJobMetricsTestJob>
    {
        private readonly MetricsTestState _state;
        public RecordingHandler(MetricsTestState state) => _state = state;

        public Task HandleAsync(
            BackgroundJobMetricsTestJob job,
            BackgroundJobExecutionContext context,
            CancellationToken ct)
        {
            _state.Handled.TrySetResult(null);
            return Task.CompletedTask;
        }
    }
}