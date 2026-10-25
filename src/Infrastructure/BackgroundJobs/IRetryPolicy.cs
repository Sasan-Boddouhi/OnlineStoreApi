using Application.BackgroundJobs;

namespace Infrastructure.BackgroundJobs;

public interface IRetryPolicy
{
    RetryDecision Evaluate(
        Exception exception,
        BackgroundJobExecutionContext context);
}
