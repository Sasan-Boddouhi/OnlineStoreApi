using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Application.BackgroundJobs;
using Xunit;

namespace OnlineStore.Tests.Unit.BackgroundJobs;

public sealed class BackgroundJobFailureMatrixTests
{
    [Fact]
    public async Task EnqueueAsync_CallerCancellationWhileQueueIsFull_ThrowsOperationCanceledException()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(1, 5, time, lifetime);

        (await queue.EnqueueAsync(new TestJob(1), "first", default)).Status
            .Should().Be(EnqueueStatus.Enqueued);

        using var cancellation = new CancellationTokenSource();
        var pending = queue.EnqueueAsync(new TestJob(2), "second", cancellation.Token).AsTask();
        cancellation.Cancel();

        var action = () => pending;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void BackgroundJobWorker_GetRetryDelay_UsesExponentialProgressionAndCap()
    {
        var worker = CreateWorker(
            new BackgroundJobOptions
            {
                RetryBaseDelaySeconds = 1,
                RetryMaxDelaySeconds = 5
            });

        var method = typeof(BackgroundJobWorker).GetMethod(
            "GetRetryDelay",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        ((TimeSpan)method.Invoke(worker, new object[] { 1 })!).Should().Be(TimeSpan.FromSeconds(1));
        ((TimeSpan)method.Invoke(worker, new object[] { 2 })!).Should().Be(TimeSpan.FromSeconds(2));
        ((TimeSpan)method.Invoke(worker, new object[] { 3 })!).Should().Be(TimeSpan.FromSeconds(4));
        ((TimeSpan)method.Invoke(worker, new object[] { 4 })!).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ScheduleAsync_EarlierRetryArrivesDuringLaterWait_WakesSchedulerEarly()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(100, 5, time, lifetime);
        var scheduler = CreateScheduler(queue, time);

        using var stop = new CancellationTokenSource();
        await scheduler.StartAsync(stop.Token);

        var late = CreateEnvelope(time);
        var early = CreateEnvelope(time);

        await scheduler.ScheduleAsync(late, TimeSpan.FromSeconds(10), stop.Token);
        time.Advance(TimeSpan.FromSeconds(1));
        await scheduler.ScheduleAsync(early, TimeSpan.FromSeconds(1), stop.Token);

        time.Advance(TimeSpan.FromSeconds(1));

        var first = await queue.DequeueAsync(
            new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

        first.JobId.Should().Be(early.JobId);

        stop.Cancel();
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ScheduleAsync_RetryInboxAtCapacity_TimeoutReturnsFalseWithoutUnboundedGrowth()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime);
        var scheduler = CreateScheduler(queue, time, retryQueueCapacity: 1, retryEnqueueTimeoutSeconds: 5);

        using var stop = new CancellationTokenSource();

        (await scheduler.ScheduleAsync(CreateEnvelope(time), TimeSpan.FromSeconds(10), stop.Token))
            .Should().BeTrue();

        var pending = scheduler.ScheduleAsync(CreateEnvelope(time), TimeSpan.FromSeconds(10), stop.Token).AsTask();
        time.Advance(TimeSpan.FromSeconds(5));

        (await pending).Should().BeFalse();
    }

    [Fact]
    public async Task RetryScheduler_MainQueueFullAtDueTime_RequeuesAfterPressureIsReleased()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(1, 1, time, lifetime);
        var scheduler = CreateScheduler(queue, time, retryEnqueueTimeoutSeconds: 1);

        await queue.EnqueueAsync(new TestJob(99), "filler", default);

        using var stop = new CancellationTokenSource();
        await scheduler.StartAsync(stop.Token);
        var retry = CreateEnvelope(time);

        await scheduler.ScheduleAsync(retry, TimeSpan.FromSeconds(1), stop.Token);
        time.Advance(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));

        var filler = await queue.DequeueAsync(default);
        filler.IdempotencyKey.Should().Be("filler");

        time.Advance(TimeSpan.FromSeconds(1));
        var requeued = await queue.DequeueAsync(
            new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

        requeued.JobId.Should().Be(retry.JobId);

        stop.Cancel();
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RetryScheduler_RequeueStarvation_ReachesTerminalCancellation()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(1, 1, time, lifetime);
        var logger = new RecordingLogger<RetryScheduler>();
        var scheduler = CreateScheduler(
            queue,
            time,
            retryEnqueueTimeoutSeconds: 1,
            maxRequeueAttempts: 3,
            logger: logger);

        await queue.EnqueueAsync(new TestJob(99), "filler", default);

        var method = typeof(RetryScheduler).GetMethod(
            "ProcessDueAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var retryItemType = typeof(RetryScheduler).Assembly.GetType(
            "Infrastructure.BackgroundJobs.RetryItem")!;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var item = Activator.CreateInstance(
                retryItemType,
                CreateEnvelope(time),
                time.GetUtcNow(),
                attempt)!;

            var task = (Task)method.Invoke(
                scheduler,
                new[] { item, CancellationToken.None })!;

            time.Advance(TimeSpan.FromSeconds(1));
            await task;
        }

        logger.Messages.Should().Contain(x =>
            x.Contains("BackgroundJobRetryQueueStarvation") &&
            x.Contains(nameof(JobCancellationReason.RetryQueueStarvation)));
    }

    [Fact]
    public async Task RequeueAsync_MainQueueFull_PreservesEnvelopeAttempt()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(
            capacity: 1,
            timeoutSeconds: 1,
            timeProvider: time,
            lifetime: lifetime);
        var envelope = CreateEnvelope(time) with { Attempt = 1 };

        (await queue.EnqueueAsync(new TestJob(99), "filler", default))
            .Should().BeEquivalentTo(new EnqueueResult(EnqueueStatus.Enqueued));

        (await queue.RequeueAsync(envelope, default))
            .Should().Be(EnqueueStatus.QueueFull);

        envelope.Attempt.Should().Be(1);

        var filler = await queue.DequeueAsync(default);
        filler.IdempotencyKey.Should().Be("key");
    }

    [Fact]
    public async Task Dispatcher_RetryEnvelopeAfterTransientFailure_ExecutesSuccessfullyWithStableIdentity()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime, timeProvider: time);
        var recorder = new RetryRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IBackgroundJobHandler<TestJob>, RetryThenSuccessHandler>();
        services.AddSingleton(
            new BackgroundJobHandlerRegistration(
                typeof(TestJob),
                typeof(RetryThenSuccessHandler),
                typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher(provider, time);

        (await queue.EnqueueAsync(new TestJob(7), "stable-key", default))
            .IsAccepted.Should().BeTrue();

        var initial = await queue.DequeueAsync(default);
        Func<Task> firstExecution = () => dispatcher.DispatchAsync(initial, default);
        await firstExecution.Should().ThrowAsync<HttpRequestException>();

        var retry = initial with { Attempt = 1 };
        (await queue.RequeueAsync(retry, default))
            .Should().Be(EnqueueStatus.Enqueued);

        var requeued = await queue.DequeueAsync(default);
        await dispatcher.DispatchAsync(requeued, default);

        recorder.JobIds.Should().HaveCount(2);
        recorder.JobIds.Distinct().Should().ContainSingle()
            .Which.Should().Be(initial.JobId);
        recorder.Attempts.OrderBy(x => x).Should().Equal(1, 2);
        recorder.IdempotencyKeys.Should().OnlyContain(x => x == "stable-key");
    }
    [Fact]
    public void StaticRetryPolicy_TimeoutBeforeMaximumAttempts_ReturnsRetry()
    {
        var policy = new StaticRetryPolicy(
            Options.Create(new BackgroundJobOptions { MaxExecutionAttempts = 5 }));

        policy.Evaluate(
            new TimeoutException(),
            new BackgroundJobExecutionContext(Guid.NewGuid(), "TestJob", 1, "key", null, null))
            .Should().Be(RetryDecision.Retry);
    }

    [Fact]
    public async Task DispatchAsync_TimeoutAtTerminalPolicy_LogsJobTimeoutCancellation()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var logger = new RecordingLogger<BackgroundJobDispatcher>();
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundJobHandler<TestJob>, BlockingHandler>();
        services.AddSingleton(new BackgroundJobHandlerRegistration(typeof(TestJob), typeof(BlockingHandler), typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher(
            provider,
            time,
            logger,
            jobTimeoutSeconds: 1);

        var task = dispatcher.DispatchAsync(CreateEnvelope(time), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));

        var action = () => task;
        await action.Should().ThrowAsync<TimeoutException>();

        logger.Messages.Should().Contain(x =>
            x.Contains("BackgroundJobCancelled") &&
            x.Contains(nameof(JobCancellationReason.JobTimeout)));
    }

    [Fact]
    public async Task DispatchAsync_HostShutdownWhileWaiting_LogsHostShutdownCancellation()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var logger = new RecordingLogger<BackgroundJobDispatcher>();
        var lifetime = new TestHostApplicationLifetime();
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundJobHandler<TestJob>, CancellationHandler>();
        services.AddSingleton(new BackgroundJobHandlerRegistration(typeof(TestJob), typeof(CancellationHandler), typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher(provider, time, logger);

        using var cancellation = new CancellationTokenSource();
        var task = dispatcher.DispatchAsync(CreateEnvelope(time), cancellation.Token);

        cancellation.Cancel();

        var action = () => task;
        await action.Should().ThrowAsync<OperationCanceledException>();
        logger.Messages.Should().Contain(x =>
            x.Contains("BackgroundJobCancelled") &&
            x.Contains(nameof(JobCancellationReason.HostShutdown)));
    }

    [Fact]
    public async Task Dispatcher_TraceAndCorrelationContext_PropagatesToHandler()
    {
        var services = new ServiceCollection();
        var recorder = new ContextRecorder();
        services.AddSingleton(recorder);
        services.AddScoped<IBackgroundJobHandler<TestJob>, ContextHandler>();
        services.AddSingleton(new BackgroundJobHandlerRegistration(typeof(TestJob), typeof(ContextHandler), typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        using var activity = new Activity("background-test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity.AddBaggage("correlation.id", "corr-123");

        var dispatcher = CreateDispatcher(provider, TimeProvider.System);
        await dispatcher.DispatchAsync(CreateEnvelope(TimeProvider.System) with { CorrelationId = "corr-123", TraceId = "trace-123" }, default);

        recorder.CorrelationId.Should().Be("corr-123");
        recorder.TraceId.Should().Be("trace-123");

        activity.Stop();
    }

    [Fact]
    public async Task Dispatcher_Success_EmitsStartedAndSucceededObservabilityEvents()
    {
        var logger = new RecordingLogger<BackgroundJobDispatcher>();
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundJobHandler<TestJob>, NoOpHandler>();
        services.AddSingleton(new BackgroundJobHandlerRegistration(typeof(TestJob), typeof(NoOpHandler), typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher(provider, TimeProvider.System, logger);
        await dispatcher.DispatchAsync(CreateEnvelope(TimeProvider.System), default);

        logger.Messages.Should().Contain(x => x.Contains("BackgroundJobStarted"));
        logger.Messages.Should().Contain(x => x.Contains("BackgroundJobSucceeded"));
    }

    [Fact]
    public async Task Dispatcher_TimeoutCancellation_EmitsCancellationReason()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var logger = new RecordingLogger<BackgroundJobDispatcher>();
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundJobHandler<TestJob>, BlockingHandler>();
        services.AddSingleton(new BackgroundJobHandlerRegistration(typeof(TestJob), typeof(BlockingHandler), typeof(TestJob).FullName!));
        using var provider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher(provider, time, logger, jobTimeoutSeconds: 1);
        var task = dispatcher.DispatchAsync(CreateEnvelope(time), default);

        time.Advance(TimeSpan.FromSeconds(1));

        var action = () => task;
        await action.Should().ThrowAsync<TimeoutException>();
        logger.Messages.Should().Contain(x => x.Contains(nameof(JobCancellationReason.JobTimeout)));
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentProducers_AcceptAllCoordinatedWrites()
    {
        const int producerCount = 8;
        const int jobsPerProducer = 10;
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(producerCount * jobsPerProducer, 1, TimeProvider.System, lifetime);
        using var barrier = new Barrier(producerCount);
        var results = new ConcurrentBag<EnqueueStatus>();

        var producers = Enumerable.Range(0, producerCount)
            .Select(producer => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < jobsPerProducer; i++)
                {
                    var result = await queue.EnqueueAsync(
                        new TestJob(producer * jobsPerProducer + i),
                        $"{producer}-{i}",
                        default);
                    results.Add(result.Status);
                }
            }))
            .ToArray();

        await Task.WhenAll(producers);

        results.Should().HaveCount(producerCount * jobsPerProducer);
        results.Should().OnlyContain(x => x == EnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task RetryScheduler_ShutdownWithPendingItems_DoesNotStartPendingRetry()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime, timeProvider: time);
        var scheduler = CreateScheduler(queue, time);

        using var stop = new CancellationTokenSource();
        await scheduler.StartAsync(stop.Token);
        var retry = CreateEnvelope(time);

        await scheduler.ScheduleAsync(retry, TimeSpan.FromSeconds(10), stop.Token);
        stop.Cancel();
        await scheduler.StopAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(20));

        var dequeue = queue.DequeueAsync(
            new CancellationTokenSource(TimeSpan.FromMilliseconds(50)).Token).AsTask();

        var action = () => dequeue;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RetryScheduler_ShutdownDuringBackoff_InterruptsWaitAndDoesNotStartRetry()
    {
        var time = new SignalingFakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime, timeProvider: time);
        var scheduler = CreateScheduler(queue, time);

        using var stoppingCts = new CancellationTokenSource();

        await scheduler.StartAsync(stoppingCts.Token);

        var retry = CreateEnvelope(time);

        (await scheduler.ScheduleAsync(
            retry,
            TimeSpan.FromSeconds(30),
            stoppingCts.Token))
            .Should().BeTrue();

        // Ensure the scheduler has entered the future-due backoff wait.
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Shutdown must interrupt the scheduler's backoff wait.
        stoppingCts.Cancel();

        var stopTask = scheduler.StopAsync(CancellationToken.None);

        var completed = await Task.WhenAny(
            stopTask,
            Task.Delay(TimeSpan.FromSeconds(2)));

        completed.Should().Be(stopTask);
        stopTask.IsCompletedSuccessfully.Should().BeTrue();

        // The pending retry must never reach the main queue.
        time.Advance(TimeSpan.FromSeconds(30));

        using var dequeueTimeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));

        var dequeueTask = queue.DequeueAsync(
            dequeueTimeout.Token).AsTask();

        await dequeueTask
            .Should()
            .ThrowAsync<OperationCanceledException>();

        scheduler.Dispose();
    }

    [Fact]
    public async Task RetryScheduler_RetryInboxStarvation_ReturnsFalseWithinConfiguredTimeout()
    {
        var time = new SignalingFakeTimeProvider(DateTimeOffset.UtcNow);
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime, timeProvider: time);
        var scheduler = CreateScheduler(
            queue,
            time,
            retryQueueCapacity: 1,
            retryEnqueueTimeoutSeconds: 1);

        using var stop = new CancellationTokenSource();

        await scheduler.ScheduleAsync(
            CreateEnvelope(time),
            TimeSpan.FromSeconds(10),
            stop.Token);

        var second = scheduler.ScheduleAsync(
            CreateEnvelope(time),
            TimeSpan.FromSeconds(10),
            stop.Token).AsTask();

        second.IsCompleted.Should().BeFalse();
        await time.TimerCreated.Task;

        time.Advance(TimeSpan.FromSeconds(1));

        (await second).Should().BeFalse();

        scheduler.Dispose();
    }

    private static ChannelBackgroundJobQueue CreateQueue(
        int capacity = 1000,
        int timeoutSeconds = 5,
        TimeProvider? timeProvider = null,
        TestHostApplicationLifetime? lifetime = null)
        => new(
            Options.Create(new ChannelBackgroundJobQueueOptions
            {
                QueueCapacity = capacity,
                EnqueueTimeoutSeconds = timeoutSeconds,
                MaxIdempotencyKeyLength = 256
            }),
            lifetime ?? new TestHostApplicationLifetime(),
            timeProvider ?? TimeProvider.System);

    private static RetryScheduler CreateScheduler(
        ChannelBackgroundJobQueue queue,
        TimeProvider time,
        int retryQueueCapacity = 100,
        int retryEnqueueTimeoutSeconds = 5,
        int maxRequeueAttempts = 10,
        RecordingLogger<RetryScheduler>? logger = null)
        => new(
            queue,
            Options.Create(new BackgroundJobOptions
            {
                RetryQueueCapacity = retryQueueCapacity,
                RetryEnqueueTimeoutSeconds = retryEnqueueTimeoutSeconds,
                RetryBaseDelaySeconds = 1,
                RetryMaxDelaySeconds = 300,
                MaxRequeueAttempts = maxRequeueAttempts
            }),
            time,
            logger ?? new RecordingLogger<RetryScheduler>());

    private static BackgroundJobWorker CreateWorker(BackgroundJobOptions options)
    {
        var lifetime = new TestHostApplicationLifetime();
        var queue = CreateQueue(lifetime: lifetime);
        var scheduler = CreateScheduler(queue, TimeProvider.System);
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundJobHandler<TestJob>, NoOpHandler>();
        var provider = services.BuildServiceProvider();

        return new BackgroundJobWorker(
            queue,
            CreateDispatcher(provider, TimeProvider.System),
            scheduler,
            new StaticRetryPolicy(Options.Create(options)),
            Options.Create(options),
            new RecordingLogger<BackgroundJobWorker>());
    }

    private static BackgroundJobDispatcher CreateDispatcher(
        ServiceProvider provider,
        TimeProvider time,
        RecordingLogger<BackgroundJobDispatcher>? logger = null,
        int jobTimeoutSeconds = 60)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetServices<BackgroundJobHandlerRegistration>(),
            time,
            Options.Create(new BackgroundJobOptions { JobTimeoutSeconds = jobTimeoutSeconds }),
            logger ?? new RecordingLogger<BackgroundJobDispatcher>());

    private static BackgroundJobEnvelope CreateEnvelope(TimeProvider time)
        => new(
            Guid.NewGuid(),
            typeof(TestJob).FullName!,
            JsonSerializer.SerializeToElement(new TestJob(1)).Clone(),
            time.GetUtcNow(),
            1,
            "key",
            null,
            null);

    private sealed record TestJob(int Value);

    private sealed class SignalingFakeTimeProvider : FakeTimeProvider
    {
        public SignalingFakeTimeProvider(DateTimeOffset start) : base(start) { }

        public TaskCompletionSource<object?> TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            TimerCreated.TrySetResult(null);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void Stop() => _stopping.Cancel();
        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class NoOpHandler : IBackgroundJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class AlwaysTransientHandler : IBackgroundJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
            => throw new HttpRequestException("transient");
    }

    private sealed class BlockingHandler : IBackgroundJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
            => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class CancellationHandler : IBackgroundJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
            => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class RetryRecorder
    {
        public ConcurrentBag<Guid> JobIds { get; } = new();
        public ConcurrentBag<int> Attempts { get; } = new();
        public ConcurrentBag<string> IdempotencyKeys { get; } = new();
        public TaskCompletionSource<object?> FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> Success { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RetryThenSuccessHandler : IBackgroundJobHandler<TestJob>
    {
        private readonly RetryRecorder _recorder;

        public RetryThenSuccessHandler(RetryRecorder recorder) => _recorder = recorder;

        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            _recorder.JobIds.Add(context.JobId);
            _recorder.Attempts.Add(context.Attempt);
            _recorder.IdempotencyKeys.Add(context.IdempotencyKey);

            if (context.Attempt == 1)
            {
                _recorder.FirstAttempt.TrySetResult(null);
                throw new HttpRequestException("transient");
            }

            _recorder.Success.TrySetResult(null);
            return Task.CompletedTask;
        }
    }

    private sealed class ContextRecorder
    {
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
    }

    private sealed class ContextHandler : IBackgroundJobHandler<TestJob>
    {
        private readonly ContextRecorder _recorder;

        public ContextHandler(ContextRecorder recorder) => _recorder = recorder;

        public Task HandleAsync(TestJob job, BackgroundJobExecutionContext context, CancellationToken cancellationToken)
        {
            _recorder.CorrelationId = context.CorrelationId;
            _recorder.TraceId = context.TraceId;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentBag<string> Messages { get; } = new();
        public TaskCompletionSource<object?> RetryScheduled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> RetrySchedulerStarvation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(message);

            if (message.Contains("BackgroundJobRetryScheduled"))
                RetryScheduled.TrySetResult(null);

            if (message.Contains("RetrySchedulerStarvation"))
                RetrySchedulerStarvation.TrySetResult(null);
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
