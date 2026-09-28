using Application.BackgroundJobs;

namespace Infrastructure.BackgroundJobs;

internal sealed record RetryItem(
    BackgroundJobEnvelope Envelope,
    DateTimeOffset DueAt,
    int RequeueAttempts);
