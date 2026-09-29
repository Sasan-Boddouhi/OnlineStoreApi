using System.Diagnostics;
using Application.BackgroundJobs;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.BackgroundProcessing;

public sealed class BackgroundJobTracePropagationTests : IClassFixture<IntegrationTestFactory<Program>>
{
    private readonly IntegrationTestFactory<Program> _factory;

    public BackgroundJobTracePropagationTests(IntegrationTestFactory<Program> factory)
        => _factory = factory;

    [Fact]
    public async Task HttpEnqueue_PropagatesTraceToBackgroundJob()
    {
        // Arrange
        var exportedActivities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => exportedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddBackgroundProcessing(
                    new ConfigurationBuilder().AddInMemoryCollection().Build());
                services.AddBackgroundJobHandler<TraceTestJob, RecordingHandler>();
                services.AddSingleton<RecordingHandlerState>();
                services.AddControllers()
                    .AddApplicationPart(typeof(TestEnqueueController).Assembly);
            });
        });

        var state = factory.Services.GetRequiredService<RecordingHandlerState>();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync("/test/enqueue", null);
        response.EnsureSuccessStatusCode();

        await state.Handled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        var summary = string.Join(
            "\n",
            exportedActivities.Select(a =>
                $"  source={a.Source.Name} | op={a.OperationName} | kind={a.Kind} | trace={a.TraceId} | span={a.SpanId} | parent={a.ParentSpanId}"));

        var jobActivity = exportedActivities
            .FirstOrDefault(a =>
                a.OperationName == "background_job.execute" &&
                a.GetTagItem("job.type")?.ToString() == typeof(TraceTestJob).FullName);

        jobActivity.Should().NotBeNull(
            $"Background job activity expected.\nExported:\n{summary}");

        var httpActivity = exportedActivities
            .FirstOrDefault(a =>
                a.Kind == ActivityKind.Server &&
                a.SpanId == jobActivity!.ParentSpanId);

        httpActivity.Should().NotBeNull(
            $"HTTP parent activity expected.\nExported:\n{summary}");

        jobActivity!.TraceId.Should().Be(httpActivity!.TraceId,
            "job must continue the HTTP trace");
        jobActivity.ParentSpanId.Should().Be(httpActivity.SpanId,
            "job's parent must be the HTTP span");

        jobActivity.GetTagItem("job.type").Should().Be(typeof(TraceTestJob).FullName);
        jobActivity.GetTagItem("job.attempt").Should().Be(1);
    }

    public sealed class RecordingHandlerState
    {
        public TaskCompletionSource<object?> Handled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingHandler : IBackgroundJobHandler<TraceTestJob>
    {
        private readonly RecordingHandlerState _state;
        public RecordingHandler(RecordingHandlerState state) => _state = state;

        public Task HandleAsync(TraceTestJob job, BackgroundJobExecutionContext context, CancellationToken ct)
        {
            _state.Handled.TrySetResult(null);
            return Task.CompletedTask;
        }
    }
}