using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OnlineStore.Tests.Shared.BackgroundProcessing;

public sealed class RecordingLogger<T> : ILogger<T>
{
    public ConcurrentBag<string> Messages { get; } = new();

    public TaskCompletionSource<object?> RetryScheduled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<object?> RetrySchedulerStarvation { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        NullScope.Instance;

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
