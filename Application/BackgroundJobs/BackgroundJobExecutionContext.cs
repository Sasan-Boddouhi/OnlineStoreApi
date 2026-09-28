namespace Application.BackgroundJobs;

public sealed record BackgroundJobExecutionContext(
    Guid JobId,
    string JobType,
    int Attempt,
    string IdempotencyKey,
    string? CorrelationId,
    string? TraceId);
