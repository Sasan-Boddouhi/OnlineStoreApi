using System.Text.Json;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Application.BackgroundJobs;
using Xunit;

namespace OnlineStore.Tests.Unit.BackgroundJobs
{
    public sealed class ChannelBackgroundJobQueueTests
    {
        [Fact]
        public async Task EnqueueAsync_MainQueueAtCapacity_ReturnsQueueFull()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var lifetime = new TestHostApplicationLifetime();
            var queue = CreateQueue(1, 1, 256, time, lifetime);

            (await queue.EnqueueAsync(new TestJob(1), "first", default)).Status
                .Should().Be(EnqueueStatus.Enqueued);

            var pending = queue.EnqueueAsync(new TestJob(2), "second", default).AsTask();
            time.Advance(TimeSpan.FromSeconds(1));

            (await pending).Status.Should().Be(EnqueueStatus.QueueFull);
        }

        [Fact]
        public async Task EnqueueAsync_AfterShutdown_ReturnsShuttingDown()
        {
            var lifetime = new TestHostApplicationLifetime();
            var queue = CreateQueue(lifetime: lifetime);

            lifetime.Stop();

            var result = await queue.EnqueueAsync(new TestJob(1), "key", default);

            result.Status.Should().Be(EnqueueStatus.ShuttingDown);
        }

        [Fact]
        public async Task EnqueueAsync_PayloadFromDisposedDocument_PayloadRemainsReadable()
        {
            var lifetime = new TestHostApplicationLifetime();
            var queue = CreateQueue(lifetime: lifetime);

            using (var document = JsonDocument.Parse("{\"value\":42}"))
            {
                var job = new JsonElementJob(document.RootElement);
                (await queue.EnqueueAsync(job, "key", default)).Status
                    .Should().Be(EnqueueStatus.Enqueued);
            }

            var envelope = await queue.DequeueAsync(default);

            envelope.Payload.GetProperty("Payload").GetProperty("value").GetInt32()
                .Should().Be(42);
        }

        [Fact]
        public async Task EnqueueAsync_NewlyEnqueuedEnvelope_AttemptIsZero()
        {
            var queue = CreateQueue();

            await queue.EnqueueAsync(new TestJob(1), "key", default);

            var envelope = await queue.DequeueAsync(default);

            envelope.Attempt.Should().Be(0);
        }

        [Fact]
        public async Task EnqueueAsync_RegisteredJob_JobTypeIsNotAssemblyQualified()
        {
            var queue = CreateQueue();

            await queue.EnqueueAsync(new TestJob(1), "key", default);

            var envelope = await queue.DequeueAsync(default);

            envelope.JobType.Should().Be(typeof(TestJob).FullName);
            envelope.JobType.Should().NotContain(",");
        }

        [Fact]
        public async Task EnqueueAsync_IdempotencyKeyExceedsMaxLength_ThrowsArgumentOutOfRange()
        {
            var queue = CreateQueue(maxIdempotencyKeyLength: 3);

            var action = () => queue.EnqueueAsync(
                new TestJob(1),
                "1234",
                default).AsTask();

            await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        [Fact]
        public async Task EnqueueAsync_IdempotencyKeyWhitespace_ThrowsArgumentException()
        {
            var queue = CreateQueue();

            var action = () => queue.EnqueueAsync(
                new TestJob(1),
                "   ",
                default).AsTask();

            await action.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EnqueueAsync_IdempotencyKeyNull_ThrowsArgumentNullException()
        {
            var queue = CreateQueue();

            var action = () => queue.EnqueueAsync(
                new TestJob(1),
                null!,
                default).AsTask();

            await action.Should().ThrowAsync<ArgumentNullException>();
        }

        private static ChannelBackgroundJobQueue CreateQueue(
            int capacity = 1000,
            int timeoutSeconds = 5,
            int maxIdempotencyKeyLength = 256,
            TimeProvider? timeProvider = null,
            TestHostApplicationLifetime? lifetime = null)
        {
            return new ChannelBackgroundJobQueue(
                Options.Create(new ChannelBackgroundJobQueueOptions
                {
                    QueueCapacity = capacity,
                    EnqueueTimeoutSeconds = timeoutSeconds,
                    MaxIdempotencyKeyLength = maxIdempotencyKeyLength
                }),
                lifetime ?? new TestHostApplicationLifetime(),
                timeProvider ?? TimeProvider.System);
        }

        private sealed record TestJob(int Value);
        private sealed record JsonElementJob(JsonElement Payload);
    }

    public sealed class StaticRetryPolicyTests
    {
        [Fact]
        public void Evaluate_HttpRequestExceptionBeforeLimit_ReturnsRetry()
        {
            var policy = new StaticRetryPolicy(Options.Create(new BackgroundJobOptions { MaxExecutionAttempts = 5 }));
            policy.Evaluate(new HttpRequestException(), Context(1))
                .Should().Be(RetryDecision.Retry);
        }

        [Fact]
        public void Evaluate_NonRetryableException_ReturnsDoNotRetry()
        {
            var policy = new StaticRetryPolicy(Options.Create(new BackgroundJobOptions { MaxExecutionAttempts = 5 }));
            policy.Evaluate(new NonRetryableJobException("permanent"), Context(1))
                .Should().Be(RetryDecision.DoNotRetry);
        }

        [Fact]
        public void Evaluate_UnknownException_ReturnsDoNotRetry()
        {
            var policy = new StaticRetryPolicy(Options.Create(new BackgroundJobOptions { MaxExecutionAttempts = 5 }));
            policy.Evaluate(new InvalidOperationException(), Context(1))
                .Should().Be(RetryDecision.DoNotRetry);
        }

        [Fact]
        public void Evaluate_ExecutionAttemptAtLimit_ReturnsDoNotRetry()
        {
            var policy = new StaticRetryPolicy(Options.Create(new BackgroundJobOptions { MaxExecutionAttempts = 5 }));
            policy.Evaluate(new TimeoutException(), Context(5))
                .Should().Be(RetryDecision.DoNotRetry);
        }

        private static BackgroundJobExecutionContext Context(int attempt) =>
            new(Guid.NewGuid(), "TestJob", attempt, "key", null, null);
    }

    public sealed class RetrySchedulerTests
    {
        [Fact]
        public async Task ScheduleAsync_DueRetry_RequeuesToMainQueue()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var lifetime = new TestHostApplicationLifetime();
            var queue = new ChannelBackgroundJobQueue(
                Options.Create(new ChannelBackgroundJobQueueOptions()),
                lifetime,
                time);

            var scheduler = new RetryScheduler(
                queue,
                Options.Create(new BackgroundJobOptions { RetryBaseDelaySeconds = 1 }),
                time,
                NullLogger<RetryScheduler>.Instance);

            using var stop = new CancellationTokenSource();
            await scheduler.StartAsync(stop.Token);

            var envelope = CreateEnvelope(time);
            (await scheduler.ScheduleAsync(envelope, TimeSpan.FromSeconds(2), stop.Token))
                .Should().BeTrue();

            time.Advance(TimeSpan.FromSeconds(2));

            var dequeued = await queue.DequeueAsync(
                new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            dequeued.JobId.Should().Be(envelope.JobId);

            stop.Cancel();
            await scheduler.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ScheduleAsync_MultipleRetries_UsesDueAtOrdering()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var lifetime = new TestHostApplicationLifetime();
            var queue = new ChannelBackgroundJobQueue(
                Options.Create(new ChannelBackgroundJobQueueOptions()),
                lifetime,
                time);
            var scheduler = new RetryScheduler(
                queue,
                Options.Create(new BackgroundJobOptions()),
                time,
                NullLogger<RetryScheduler>.Instance);

            using var stop = new CancellationTokenSource();
            await scheduler.StartAsync(stop.Token);

            var late = CreateEnvelope(time);
            var early = CreateEnvelope(time);

            await scheduler.ScheduleAsync(late, TimeSpan.FromSeconds(10), stop.Token);
            await scheduler.ScheduleAsync(early, TimeSpan.FromSeconds(2), stop.Token);

            time.Advance(TimeSpan.FromSeconds(2));

            var first = await queue.DequeueAsync(
                new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);
            first.JobId.Should().Be(early.JobId);

            time.Advance(TimeSpan.FromSeconds(8));

            var second = await queue.DequeueAsync(
                new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);
            second.JobId.Should().Be(late.JobId);

            stop.Cancel();
            await scheduler.StopAsync(CancellationToken.None);
        }

        private static BackgroundJobEnvelope CreateEnvelope(TimeProvider time) =>
            new(
                Guid.NewGuid(),
                "TestJob",
                JsonSerializer.SerializeToElement(new { Value = 1 }).Clone(),
                time.GetUtcNow(),
                1,
                "key",
                null,
                null);
    }

    public sealed class BackgroundJobDispatcherTests
    {
        [Fact]
        public async Task DispatchAsync_RegisteredHandler_ExecutesJob()
        {
            var recorder = new HandlerRecorder();
            var services = new ServiceCollection();
            services.AddSingleton(recorder);
            services.AddScoped<IBackgroundJobHandler<TestJob>, RecordingHandler>();
            services.AddSingleton(new BackgroundJobHandlerRegistration(
                typeof(TestJob),
                typeof(RecordingHandler),
                typeof(TestJob).FullName!));

            using var provider = services.BuildServiceProvider();
            var dispatcher = new BackgroundJobDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetServices<BackgroundJobHandlerRegistration>(),
                TimeProvider.System,
                Options.Create(new BackgroundJobOptions()),
                NullLogger<BackgroundJobDispatcher>.Instance);

            var envelope = new BackgroundJobEnvelope(
                Guid.NewGuid(),
                typeof(TestJob).FullName!,
                JsonSerializer.SerializeToElement(new TestJob(7)).Clone(),
                DateTimeOffset.UtcNow,
                0,
                "key",
                "corr",
                "trace");

            await dispatcher.DispatchAsync(envelope, default);

            recorder.Value.Should().Be(7);
            recorder.Attempt.Should().Be(1);
        }

        [Fact]
        public async Task DispatchAsync_MissingHandler_ThrowsInvalidOperationException()
        {
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var dispatcher = new BackgroundJobDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Array.Empty<BackgroundJobHandlerRegistration>(),
                TimeProvider.System,
                Options.Create(new BackgroundJobOptions()),
                NullLogger<BackgroundJobDispatcher>.Instance);

            var envelope = new BackgroundJobEnvelope(
                Guid.NewGuid(),
                "Missing.Job",
                JsonSerializer.SerializeToElement(new { }).Clone(),
                DateTimeOffset.UtcNow,
                0,
                "key",
                null,
                null);

            var action = () => dispatcher.DispatchAsync(envelope, default);

            await action.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task DispatchAsync_MalformedPayload_ThrowsJsonException()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new HandlerRecorder());
            services.AddScoped<IBackgroundJobHandler<TestJob>, RecordingHandler>();
            services.AddSingleton(new BackgroundJobHandlerRegistration(
                typeof(TestJob),
                typeof(RecordingHandler),
                typeof(TestJob).FullName!));

            using var provider = services.BuildServiceProvider();
            var dispatcher = new BackgroundJobDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetServices<BackgroundJobHandlerRegistration>(),
                TimeProvider.System,
                Options.Create(new BackgroundJobOptions()),
                NullLogger<BackgroundJobDispatcher>.Instance);

            using var document = JsonDocument.Parse("{\"unexpected\":true}");
            var envelope = new BackgroundJobEnvelope(
                Guid.NewGuid(),
                typeof(TestJob).FullName!,
                document.RootElement.Clone(),
                DateTimeOffset.UtcNow,
                0,
                "key",
                null,
                null);

            var action = () => dispatcher.DispatchAsync(envelope, default);

            await action.Should().ThrowAsync<JsonException>();
        }

        private sealed record TestJob(int Value);

        private sealed class HandlerRecorder
        {
            public int Value { get; set; }
            public int Attempt { get; set; }
        }

        private sealed class RecordingHandler : IBackgroundJobHandler<TestJob>
        {
            private readonly HandlerRecorder _recorder;

            public RecordingHandler(HandlerRecorder recorder) => _recorder = recorder;

            public Task HandleAsync(
                TestJob job,
                BackgroundJobExecutionContext context,
                CancellationToken cancellationToken)
            {
                _recorder.Value = job.Value;
                _recorder.Attempt = context.Attempt;
                return Task.CompletedTask;
            }
        }
    }

    internal sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Stop() => _stopping.Cancel();

        public void StopApplication() => _stopping.Cancel();
    }
}
