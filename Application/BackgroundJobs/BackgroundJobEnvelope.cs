using System.Text.Json;

namespace Application.BackgroundJobs;

public sealed record BackgroundJobEnvelope(
    Guid JobId,
    string JobType,
    JsonElement Payload,
    DateTimeOffset EnqueuedAt,
    int Attempt,
    string IdempotencyKey,
    string? CorrelationId,
    string? TraceId,
    string? ParentSpanId = null);