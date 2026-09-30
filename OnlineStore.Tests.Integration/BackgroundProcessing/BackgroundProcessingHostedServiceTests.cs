using System.Collections.Concurrent;
using System.Diagnostics;
using Application.BackgroundJobs;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.BackgroundProcessing;

public sealed class BackgroundProcessingHostedServiceTests : IClassFixture<IntegrationTestFactory<Program>>
{
    private readonly IntegrationTestFactory<Program> _factory;

    public BackgroundProcessingHostedServiceTests(IntegrationTestFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task RetryScheduler_HostShutdownDuringBackoff_DoesNotExecuteRetry()
    {
        using var factory = CreateFactory<TransientThenSuccessHandler>(retryBaseDelaySeconds: 10);
        _ = factory.CreateClient();
        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var recorder = factory.Services.GetRequiredService<RetryAttemptRecorder>();
        var logger = factory.Services.GetRequiredService<RecordingLogger<BackgroundJobWorker>>();
        (await queue.EnqueueAsync(new TestJob(1), "f20-shutdown", default)).IsAccepted.Should().BeTrue();
        await recorder.FirstAttemptFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await logger.RetryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        recorder.ExecutionCount.Should().Be(1);
        var stopwatch = Stopwatch.StartNew();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        lifetime.ApplicationStopped.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)).Should().BeTrue("Host must shut down within bounded time");
        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        recorder.ExecutionCount.Should().Be(1, "retry must not start during shutdown");
    }

    [Fact]
    public async Task Worker_HostShutdownWhileWaiting_StopsPromptly()
    {
        using var factory = CreateFactory<WaitingJobHandler>();
        _ = factory.CreateClient();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        lifetime.ApplicationStopped.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public async Task Worker_GracefulShutdown_DrainsAcceptedWorkBeforeStopCompletes()
    {
        using var factory = CreateFactory<DrainJobHandler>();
        _ = factory.CreateClient();

        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var recorder = factory.Services.GetRequiredService<DrainRecorder>();

        (await queue.EnqueueAsync(new TestJob(1), "drain-1", default)).IsAccepted.Should().BeTrue();
        (await queue.EnqueueAsync(new TestJob(2), "drain-2", default)).IsAccepted.Should().BeTrue();

        await recorder.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        recorder.ReleaseFirst.TrySetResult(null);
        lifetime.ApplicationStopped.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)).Should().BeTrue();
        recorder.Executed.Should().Be(2);
    }

    [Fact]
    public async Task Dispatcher_ScopedHandler_CreatesSeparateScopePerExecution()
    {
        using var factory = CreateFactory<ScopedHandler>();
        _ = factory.CreateClient();

        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var recorder = factory.Services.GetRequiredService<ScopedInstanceRecorder>();

        (await queue.EnqueueAsync(new TestJob(1), "scope-1", default)).IsAccepted.Should().BeTrue();
        (await queue.EnqueueAsync(new TestJob(2), "scope-2", default)).IsAccepted.Should().BeTrue();

        await recorder.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        recorder.InstanceIds.Should().HaveCount(2);
        recorder.InstanceIds.Should().OnlyHaveUniqueItems();

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.StopApplication();
        lifetime.ApplicationStopped.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public void RegistrationValidator_NoHandler_FailsApplicationStartup()
    {
        using var factory = CreateFactoryWithoutHandlers();
        Action action = () => factory.CreateClient();

        action.Should().Throw<Exception>()
            .Where(x => x.ToString().Contains("background job handler"));
    }

    [Fact]
    public void RegistrationValidator_DuplicateHandler_FailsApplicationStartup()
    {
        using var factory = CreateFactoryWithDuplicateHandlers();
        Action action = () => factory.CreateClient();

        action.Should().Throw<Exception>()
            .Where(x => x.ToString().Contains("background job handler"));
    }

    private WebApplicationFactory<Program> CreateFactory<THandler>(int? retryBaseDelaySeconds = null)
        where THandler : class, IBackgroundJobHandler<TestJob>
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<DrainRecorder>();
            services.AddSingleton<ScopedInstanceRecorder>();
            services.AddSingleton<RetryAttemptRecorder>();
            services.AddScoped<ScopedMarker>();
            var values = new Dictionary<string, string?>();
            if (retryBaseDelaySeconds.HasValue)
                values["BackgroundJobs:RetryBaseDelaySeconds"] = retryBaseDelaySeconds.Value.ToString();
            services.AddBackgroundProcessing(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            services.AddBackgroundJobHandler<TestJob, THandler>();
            if (retryBaseDelaySeconds.HasValue)
            {
                services.AddSingleton<RecordingLogger<BackgroundJobWorker>>();
                services.AddSingleton<ILogger<BackgroundJobWorker>>(sp => sp.GetRequiredService<RecordingLogger<BackgroundJobWorker>>());
            }
        }));

    private WebApplicationFactory<Program> CreateFactoryWithoutHandlers()
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddBackgroundProcessing(
                    new ConfigurationBuilder().AddInMemoryCollection().Build())));

    private WebApplicationFactory<Program> CreateFactoryWithDuplicateHandlers()
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddBackgroundProcessing(
                    new ConfigurationBuilder().AddInMemoryCollection().Build());
                services.AddBackgroundJobHandler<TestJob, WaitingJobHandler>();
                services.AddBackgroundJobHandler<TestJob, DrainJobHandler>();
            }));

    public sealed record TestJob(int Value);

    public sealed class RetryAttemptRecorder
    {
        private int _executionCount;
        public int ExecutionCount => Volatile.Read(ref _executionCount);
        public TaskCompletionSource<object?> FirstAttemptFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Increment() => Interlocked.Increment(ref _executionCount);
    }

    public sealed class TransientThenSuccessHandler : IBackgroundJobHandler<TestJob>
    {
        private readonly RetryAttemptRecorder _recorder;
        public TransientThenSuccessHandler(RetryAttemptRecorder recorder) => _recorder = recorder;
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            if (_recorder.Increment() == 1)
            {
                _recorder.FirstAttemptFailed.TrySetResult(null);
                throw new HttpRequestException("transient");
            }
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentBag<string> Messages { get; } = new();
        public TaskCompletionSource<object?> RetryScheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(message);
            if (message.Contains("BackgroundJobRetryScheduled")) RetryScheduled.TrySetResult(null);
        }
        private sealed class NullScope : IDisposable { public static NullScope Instance { get; } = new(); public void Dispose() { } }
    }

    public sealed class WaitingJobHandler : IBackgroundJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class DrainRecorder
    {
        private int _executed;
        public int Executed => Volatile.Read(ref _executed);
        public TaskCompletionSource<object?> FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Increment() => Interlocked.Increment(ref _executed);
    }

    public sealed class DrainJobHandler : IBackgroundJobHandler<TestJob>
    {
        private readonly DrainRecorder _recorder;
        public DrainJobHandler(DrainRecorder recorder) => _recorder = recorder;

        public async Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            if (_recorder.Increment() == 1)
            {
                _recorder.FirstStarted.TrySetResult(null);
                await _recorder.ReleaseFirst.Task.ConfigureAwait(false);
            }
        }
    }

    public sealed class ScopedInstanceRecorder
    {
        public ConcurrentBag<Guid> InstanceIds { get; } = new();
        public TaskCompletionSource<object?> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class ScopedHandler : IBackgroundJobHandler<TestJob>
    {
        private readonly ScopedInstanceRecorder _recorder;
        private readonly ScopedMarker _marker;

        public ScopedHandler(ScopedInstanceRecorder recorder, ScopedMarker marker)
        {
            _recorder = recorder;
            _marker = marker;
        }

        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            _recorder.InstanceIds.Add(_marker.Id);
            if (_recorder.InstanceIds.Count >= 2)
                _recorder.Completed.TrySetResult(null);
            return Task.CompletedTask;
        }
    }
}
