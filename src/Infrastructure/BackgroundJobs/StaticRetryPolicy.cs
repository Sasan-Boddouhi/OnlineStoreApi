using System.Net.Http;
using Application.BackgroundJobs;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

public sealed class StaticRetryPolicy : IRetryPolicy
{
    private readonly BackgroundJobOptions _options;

    public StaticRetryPolicy(IOptions<BackgroundJobOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public RetryDecision Evaluate(
        Exception exception,
        BackgroundJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Attempt >= _options.MaxExecutionAttempts)
            return RetryDecision.DoNotRetry;

        return exception switch
        {
            NonRetryableJobException => RetryDecision.DoNotRetry,
            HttpRequestException => RetryDecision.Retry,
            TimeoutException => RetryDecision.Retry,
            IOException => RetryDecision.Retry,
            _ => RetryDecision.DoNotRetry
        };
    }
}
