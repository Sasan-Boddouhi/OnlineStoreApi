using System.Collections.Concurrent;
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
    public async Task RetryScenario_EmitsRetryScheduledEventAndRetryMetrics()
    {
        var targetJobType = typeof(RetryMetricsTestJob).FullName!;
        var capturedRetries = new ConcurrentBag<(long Value, string? JobType)>();
        var capturedDelays = new ConcurrentBag<(double Value, string? JobType)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == OnlineStoreMetrics.BackgroundJobsMeterName) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { if (instrument.Name == "background_jobs.retried") capturedRetries.Add((value, ExtractJobType(tags))); });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => { if (instrument.Name == "background_jobs.retry.delay") capturedDelays.Add((value, ExtractJobType(tags))); });
        listener.Start();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<RetryMetricsState>();
            services.AddSingleton<RecordingLogger<BackgroundJobWorker>>();
            services.AddSingleton<ILogger<BackgroundJobWorker>>(sp => sp.GetRequiredService<RecordingLogger<BackgroundJobWorker>>());
            services.AddBackgroundProcessing(new ConfigurationBuilder().AddInMemoryCollection().Build());
            services.AddBackgroundJobHandler<RetryMetricsTestJob, RetryThenSuccessHandler>();
        }));
        _ = factory.CreateClient();
        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var state = factory.Services.GetRequiredService<RetryMetricsState>();
        var logger = factory.Services.GetRequiredService<RecordingLogger<BackgroundJobWorker>>();
        (await queue.EnqueueAsync(new RetryMetricsTestJob(42), "f28-retry", default)).IsAccepted.Should().BeTrue();
        await state.SecondExecutionReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await logger.RetryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        logger.Messages.Where(m => m.Contains("BackgroundJobRetryScheduled")).Where(m => m.Contains(targetJobType)).Should().ContainSingle();
        capturedRetries.Where(t => t.JobType == targetJobType).Sum(t => t.Value).Should().Be(1);
        var matchingDelays = capturedDelays.Where(t => t.JobType == targetJobType).ToList();
        matchingDelays.Should().ContainSingle();
        matchingDelays.Single().Value.Should().BeGreaterThan(0);
    }

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

    private static string? ExtractJobType(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags) if (tag.Key == "job.type") return tag.Value?.ToString();
        return null;
    }

    public sealed record RetryMetricsTestJob(int Value);
    public sealed class RetryMetricsState { public TaskCompletionSource<object?> SecondExecutionReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public sealed class RetryThenSuccessHandler : IBackgroundJobHandler<RetryMetricsTestJob>
    {
        private readonly RetryMetricsState _state;
        public RetryThenSuccessHandler(RetryMetricsState state) => _state = state;
        public Task HandleAsync(RetryMetricsTestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.Attempt == 1) throw new HttpRequestException("transient");
            _state.SecondExecutionReached.TrySetResult(null);
            return Task.CompletedTask;
        }
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