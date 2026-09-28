# Background Processing — Design Specification

**Status:** Approved Design — Pending Implementation  
**Document:** `docs/specs/background-processing.md`  
**Target Framework:** .NET 8 / ASP.NET Core 8  
**Scope:** In-process background job processing infrastructure

## 1. Purpose

This specification defines an in-process background job processing subsystem with bounded queuing, backpressure, asynchronous execution, centralized retry policy, delayed retries, graceful shutdown, cancellation semantics, idempotency metadata, structured observability, and DI validation.

Application contracts remain independent of `Channel<T>`, `BackgroundService`, schedulers, and future external brokers.

## 2. Goals

- Bounded in-memory execution queue.
- Strongly typed job handlers.
- Persistable job envelope.
- Stable job type identifiers.
- Caller-supplied idempotency keys.
- Centralized retry classification.
- Non-retryable escape hatch.
- Delayed retries without blocking the main worker.
- Graceful shutdown with bounded draining.
- Structured observability and trace correlation.
- Startup validation of handler registrations.
- Replaceable transport/execution infrastructure.

## 3. Non-Goals

This milestone does not implement RabbitMQ, Kafka, Azure Service Bus, Redis-backed queues, database-backed queues, persistent retries, Outbox Pattern, distributed scheduling, distributed idempotency storage, durable DLQ, Hangfire, Quartz, circuit breakers, distributed worker coordination, cross-node ownership, automatic horizontal scaling, or guaranteed recovery of in-memory jobs after process termination.

## 4. Architecture

```text
Application
    |
IBackgroundJobQueue
    |
BackgroundJobEnvelope
    |
Infrastructure Bounded Channel
    |
BackgroundJobWorker
    |
Job Dispatcher
    |
IBackgroundJobHandler<TJob>
```

Retry path:

```text
Worker -> Retry Scheduler (BackgroundService)
             -> bounded Retry Inbox
             -> PriorityQueue ordered by DueAt
             -> Main Queue
```

Application owns contracts. Infrastructure owns transport, workers, scheduler, dispatching, retry policy, and DI validation. Web/API translates queue results to HTTP responses.

## 5. BackgroundJobEnvelope

```csharp
public sealed record BackgroundJobEnvelope(
    Guid JobId,
    string JobType,
    JsonElement Payload,
    DateTimeOffset EnqueuedAt,
    int Attempt,
    string IdempotencyKey,
    string? CorrelationId,
    string? TraceId);
```

Invariants:
- `JobId` is stable across retries.
- `IdempotencyKey` is stable across retries.
- `Attempt` is 0 when the envelope is initially enqueued.
- `Attempt` is incremented to 1 immediately before the first handler invocation and thereafter once per actual execution.
- `JobType` is a stable contractual identifier.
- `Payload` is independent of its source `JsonDocument` lifetime.
- Payload obtained from a `JsonDocument` must use `JsonElement.Clone()`. (D-020)

The envelope is designed to be serialization/persistence-safe. (D-012)

### Application dependency note

The Application project targets .NET 8 and has no explicit `System.Text.Json` package reference; `JsonElement` is provided by the .NET shared framework. Its use in the Application contract is intentional because the envelope must retain a serialization/persistence-safe JSON payload while remaining independent of a concrete queue transport. (D-012, D-020)

The repository convention for this layer is the `Application.*` namespace root; the background-job contracts therefore use `Application.BackgroundJobs`.

## 6. Job Identity

`JobId` identifies a queued execution, `IdempotencyKey` identifies the logical operation, and `Attempt` identifies the number of executions that have started.

An initially enqueued envelope has `Attempt = 0`. The Worker/Dispatcher increments it immediately before handler invocation, so the first execution observes `Attempt = 1`. Retry keeps JobId and IdempotencyKey and increments Attempt only when the retry execution actually starts.

## 7. Idempotency Contract

The caller supplies the idempotency key. The Application queue contract is producer-only; consumer-side dequeue is intentionally kept out of the Application contract and belongs to an Infrastructure-internal consumer abstraction. (D-028, D-041)

```csharp
public interface IBackgroundJobQueue
{
    ValueTask<EnqueueResult> EnqueueAsync<TJob>(
        TJob job,
        string idempotencyKey,
        CancellationToken cancellationToken);

}
```

Keys must be non-null, non-empty, non-whitespace, and bounded by configuration. Queue transport does not enforce business uniqueness; application/handler logic owns enforcement. (D-028)

## 8. Queue Semantics

The execution queue is a bounded `Channel<T>` with:
- Capacity: 1000 proposed MVP default.
- Full mode: `Wait`.
- Enqueue timeout: 5 seconds proposed MVP default.

If capacity is unavailable within the timeout, return `QueueFull`. The API layer may map this to HTTP 503. The queue has no HTTP dependency. (D-013, D-022)

## 9. EnqueueResult

```csharp
public enum EnqueueStatus
{
    Enqueued,
    QueueFull,
    ShuttingDown
}

public readonly record struct EnqueueResult(EnqueueStatus Status)
{
    public bool IsAccepted => Status == EnqueueStatus.Enqueued;
}
```

Normal queue pressure is represented as a result, not an exception. (D-021)

## 10. Job Lifecycle / State Machine

```text
Created -> Enqueued -> Processing -> Succeeded
                         |
                         +-> WaitingForRetry -> Retry Scheduler -> Enqueued
                         |
                         +-> Failed
                         |
                         +-> Cancelled
```

A timeout may lead to retry or terminal cancellation according to policy. A retry enqueue is observable as a retry scheduling event followed by a new started execution.

## 11. Cancellation Semantics

```csharp
public enum JobCancellationReason
{
    HostShutdown,
    RetryQueueStarvation,
    RetrySchedulerStarvation,
    JobTimeout
}
```

Host shutdown, job timeout, retry queue starvation, and retry scheduler starvation are distinct reasons.

## 12. Retry Policy

Retry is an Infrastructure concern. Handlers do not implement retry loops.

```csharp
public interface IRetryPolicy
{
    RetryDecision Evaluate(
        Exception exception,
        BackgroundJobExecutionContext context);
}
```

MVP uses a static policy behind this abstraction. (D-016)

## 13. Retry Classification

Retryable examples:
- `HttpRequestException`
- `TimeoutException`
- explicitly recognized transient I/O failures
- job execution timeout, subject to policy

Permanent examples:
- `NonRetryableJobException`
- validation/business failures
- malformed payload
- serialization failures
- handler configuration failures
- unknown/unclassified exceptions

Unknown exceptions are permanent by default. (D-018)

## 14. NonRetryableJobException

```csharp
public sealed class NonRetryableJobException : Exception
{
    public NonRetryableJobException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
```

Handlers may use this escape hatch when they can classify an otherwise ambiguous failure as permanently non-retryable. (D-007)

## 15. Retry Backoff

Execution retry uses exponential backoff:

```text
delay = min(baseDelay * 2^(attempt - 1), maximumDelay)
```

Proposed MVP defaults:
- Base delay: 1 second.
- Maximum delay: 5 minutes.
- Maximum execution attempts: 5.

The execution worker does not block during backoff. Jitter is deferred from the MVP for deterministic testing.

## 16. Retry Scheduler

The retry scheduler is a dedicated `BackgroundService`.

It accepts retry items, orders them by `DueAt`, wakes when the earliest item is due, and submits due jobs to the main queue. (D-026, D-029)

## 17. Retry Scheduler Synchronization

External producers never mutate the `PriorityQueue` directly.

```text
External producer
    -> bounded Retry Inbox
    -> Scheduler loop
    -> PriorityQueue<RetryItem, DateTimeOffset>
```

The PriorityQueue has a single logical owner. The scheduler uses an internal wake-up signal so a newly inserted earlier DueAt can interrupt its current wait.

Retry scheduler capacity is 100 proposed MVP default. (D-025)

The retry inbox uses bounded `Wait` semantics with a configurable enqueue timeout. If the retry inbox cannot accept an item within that timeout, the scheduler does not drop the retry. The retry remains owned by the producer until it can be accepted or the bounded retry-scheduler starvation policy terminates it.

## 18. Retry → Main Queue Backpressure

A due retry attempts to enter the main queue. If the queue is full, the scheduler does not block indefinitely. It holds the retry item and retries after a bounded delay. (D-027)

## 19. Requeue Attempts

`Attempt` and `RequeueAttempts` are separate.

- Attempt = actual execution count.
- RequeueAttempts = attempts to move a due retry into the main queue.

Proposed MVP defaults:
- Maximum RequeueAttempts: 10.
- Requeue backoff cap: 5 minutes.

Example delays: 1s, 2s, 4s, 8s, 30s, 60s, 120s, 240s, 300s, 300s.

If the retry cannot enter the main queue after the limit, it becomes `Cancelled` with `RetryQueueStarvation`.

## 20. Shutdown Behavior

Shutdown:
1. Stops accepting new jobs.
2. Drains accepted work.
3. Ends when `HostOptions.ShutdownTimeout` is reached.

In-memory jobs remaining after the shutdown deadline are not durable and are not guaranteed to survive process termination. Hosted services must honor the host cancellation token.

## 21. Handler Contract

```csharp
public interface IBackgroundJobHandler<in TJob>
{
    Task HandleAsync(
        TJob job,
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record BackgroundJobExecutionContext(
    Guid JobId,
    string JobType,
    int Attempt,
    string IdempotencyKey,
    string? CorrelationId,
    string? TraceId);
```

The contract exposes no queue, scheduler, retry, or HTTP implementation details.

## 22. Job Dispatcher

The dispatcher belongs to Infrastructure. It identifies the job type, resolves exactly one handler from DI, creates execution context, invokes the handler, and returns the execution outcome.

The dispatcher consumes jobs through an Infrastructure-internal consumer abstraction; `IBackgroundJobQueue` remains producer-only at the Application boundary. (D-041)

It contains no business logic.

## 23. DI Validation

Startup validation requires exactly one handler per registered job type:

- 0 handlers -> startup failure.
- 1 handler -> valid.
- More than 1 -> startup failure.

Configuration errors are detected before traffic reaches the application. (D-014)

## 24. Worker Lifecycle

The primary worker is a `BackgroundService`:

```text
ExecuteAsync(stoppingToken)
    -> Dequeue
    -> Create DI scope
    -> Resolve handler
    -> Create context
    -> Execute
    -> Success / Retry / Failure / Cancellation
```

A separate DI scope is created for each execution.

## 25. Observability

Structured events must include, where available:
- JobId
- JobType
- Attempt
- IdempotencyKey
- CorrelationId
- TraceId
- Outcome
- Duration
- CancellationReason
- ExceptionType

Recommended events:
- `BackgroundJobStarted`
- `BackgroundJobSucceeded`
- `BackgroundJobRetryScheduled`
- `BackgroundJobFailed`
- `BackgroundJobCancelled`
- `BackgroundJobRetryQueueStarvation`

## 26. Metrics

Reserved metric names:

```text
background_jobs_enqueued_total
background_jobs_completed_total
background_jobs_failed_total
background_jobs_cancelled_total
background_jobs_retried_total
background_jobs_enqueue_rejected_total
background_jobs_queue_depth
background_jobs_execution_duration_seconds
background_jobs_retry_delay_seconds
background_jobs_retry_queue_depth
background_jobs_retry_queue_starvation_total
```

Instrumentation remains exporter-independent.

## 27. Trace Correlation

When a job is created inside an HTTP request, TraceId and CorrelationId should be propagated into the envelope and execution context.

No specific tracing exporter is required by this contract.

## 28. Failure States

- `Succeeded`: handler completed successfully.
- `Failed`: permanent failure or retry exhaustion.
- `Cancelled`: execution/scheduling was intentionally stopped, with a cancellation reason.
- `DeadLettered`: reserved for future durable DLQ; not active in MVP.
- `Abandoned`: reserved future concept; not active in MVP.

## 29. Configuration Defaults

The following are **proposed MVP defaults**. They are not business requirements and may be overridden per deployment or per job type in future milestones.

```json
{
  "BackgroundJobs": {
    "QueueCapacity": 1000,
    "EnqueueTimeoutSeconds": 5,
    "RetryQueueCapacity": 100,
    "RetryEnqueueTimeoutSeconds": 5,
    "RetryBaseDelaySeconds": 1,
    "RetryMaxDelaySeconds": 300,
    "MaxExecutionAttempts": 5,
    "MaxRequeueAttempts": 10,
    "JobTimeoutSeconds": 60
  }
}
```

## 30. Testing Requirements

The implementation must be independently testable for queue capacity, enqueue timeout, concurrency, retry classification, backoff, scheduler ordering/wake-up, retry starvation, idempotency propagation, DI validation, timeout, shutdown, drain behavior, and cancellation reasons.

Detailed test organization is defined in the Testing Strategy milestone.

## 31. Deferred / Open Questions

Deferred to implementation or later milestones:
1. Exact PriorityQueue synchronization implementation.
2. Exact retry inbox implementation.
3. Exact DI registration API.
4. Exact JSON serializer options.
5. Concrete OpenTelemetry metric instruments.
6. Concrete Activity naming.
7. Concrete application job/use case.
8. Exact handler discovery mechanism.
9. Per-job timeout override mechanism
10. Exact retry-inbox enqueue timeout behavior if deployment-level tuning requires a different default..

Future milestones may add durable retries, durable DLQ, Outbox, distributed idempotency, external brokers, multi-node coordination, and parallel workers.

## 32. Decision Traceability

- **D-007** — Retry classification lives in Infrastructure policy; handlers use `NonRetryableJobException` as an escape hatch.
- **D-011** — Every abstraction must have a clear responsibility boundary and remain independently testable.
- **D-012** — The envelope must have a persistable shape independent of the current in-memory transport.
- **D-013** — The main queue is bounded; queue pressure is surfaced as `QueueFull` and may map to HTTP 503.
- **D-014** — Handler registrations are validated at startup; zero or multiple handlers are configuration errors.
- **D-015** — Shutdown uses graceful draining with a bounded host shutdown timeout.
- **D-016** — MVP retry classification uses a static Infrastructure policy behind `IRetryPolicy`.
- **D-017** — `JobType` is a stable contractual identifier and not an assembly-qualified CLR name.
- **D-018** — Unknown or unclassified exceptions are permanent by default.
- **D-019** — Transient failures use delayed scheduling instead of direct worker re-enqueue.
- **D-020** — Payloads must use `JsonElement.Clone()` to detach from `JsonDocument` lifetime.
- **D-021** — Normal enqueue control flow uses typed `EnqueueResult`, not queue-full exceptions.
- **D-022** — Main queue capacity is 1000 as the proposed MVP default.
- **D-023** — `Cancelled` is an explicit lifecycle state with a cancellation reason.
- **D-024** — Host shutdown interrupts retry backoff and does not start new delayed retries.
- **D-025** — Retry scheduler capacity is 100 as the proposed MVP default.
- **D-026** — Retry scheduling uses a single loop with a `PriorityQueue` ordered by `DueAt`.
- **D-027** — Due retries use bounded requeue attempts when the main queue is full.
- **D-028** — `IdempotencyKey` is caller-supplied and stable across retries; enforcement belongs to application logic.
- **D-029** — Retry Scheduler is a dedicated `BackgroundService`.
- **D-031** — `Attempt` is 0 at initial enqueue and increments at actual execution start in Worker/Dispatcher.
- **D-032** — Scheduler timing is driven by `TimeProvider` so deterministic tests can use `FakeTimeProvider`.
- **D-033** — MVP retry jitter is intentionally disabled and is a documented high-concurrency limitation.
- **D-034** — Coverage is risk-based with no global numeric gate.
- **D-035** — Concurrency tests use deterministic synchronization rather than timing assumptions.
- **D-036** — Test methods use `MethodName_Scenario_ExpectedOutcome` naming.
- **D-037** — Background Processing tests preserve repository-wide parallelism through isolated test state.
- **D-038** — `JobType` must be a stable contract name independent of CLR assembly-qualified names.
- **D-039** — `IdempotencyKey` validation rejects null, whitespace, and values exceeding configured maximum length.
- **D-040** — Retry inbox is bounded with `Wait` semantics and timeout; failed acceptance terminates with `RetrySchedulerStarvation` rather than dropping a retry.
- **D-041** — `IBackgroundJobQueue` exposes a producer-only Application contract. `DequeueAsync` belongs to an Infrastructure-internal consumer interface to enforce D-011's clear responsibility boundary.

## 33. Contract Signatures

### 33.1 Application Contracts

The Application-facing queue contract is producer-only. Consumer-side dequeue is intentionally kept out of the Application contract. (D-041)

```csharp
public interface IBackgroundJobQueue
{
    ValueTask<EnqueueResult> EnqueueAsync<TJob>(
        TJob job,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IBackgroundJobHandler<in TJob>
{
    Task HandleAsync(
        TJob job,
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record BackgroundJobExecutionContext(
    Guid JobId,
    string JobType,
    int Attempt,
    string IdempotencyKey,
    string? CorrelationId,
    string? TraceId);
```

### 33.2 Infrastructure Contracts

These contracts are owned by Infrastructure and are not exposed by the Application project. The queue consumer abstraction provides the dequeue boundary used by the worker/dispatcher. (D-011, D-041)

```csharp
public interface IBackgroundJobConsumer
{
    ValueTask<BackgroundJobEnvelope> DequeueAsync(
        CancellationToken cancellationToken);
}

public interface IRetryPolicy
{
    RetryDecision Evaluate(
        Exception exception,
        BackgroundJobExecutionContext context);
}
```

The remaining shared result and exception types are:

```csharp
public enum EnqueueStatus
{
    Enqueued,
    QueueFull,
    ShuttingDown
}

public readonly record struct EnqueueResult(EnqueueStatus Status)
{
    public bool IsAccepted => Status == EnqueueStatus.Enqueued;
}

public enum JobCancellationReason
{
    HostShutdown,
    RetryQueueStarvation,
    RetrySchedulerStarvation,
    JobTimeout
}

public sealed class NonRetryableJobException : Exception
{
    public NonRetryableJobException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
```

The final shape of `RetryDecision` is an implementation detail, constrained by the MVP decision in the Testing/Implementation phase to a binary retry/no-retry decision.
## 34. Design Constraints

The implementation must preserve these invariants:

1. Application code must not depend on `Channel<T>`.
2. Application code must not depend on `BackgroundService`.
3. Application code must not depend on the retry scheduler.
4. Handlers must not implement retry loops.
5. JobId remains stable across retries.
6. IdempotencyKey remains stable across retries.
7. Attempt is 0 at initial enqueue and counts actual executions after increment at execution start, not queue push attempts.
8. RequeueAttempts remains separate from Attempt.
9. Unknown exceptions are not retried by default.
10. Host-shutdown cancellation is not retried.
11. Job timeout, host shutdown, retry queue starvation, and retry scheduler starvation remain distinguishable.
12. PriorityQueue has one logical owner.
13. External producers communicate with the scheduler through its inbox.
14. Retry memory remains bounded.
15. Main queue capacity remains bounded.
16. Queue pressure does not require exceptions for normal control flow.
17. HTTP status codes remain outside queue infrastructure.
18. Payload lifetime is independent of JsonDocument.
19. JobType is independent of CLR assembly names.
20. Shutdown does not start new delayed retries.
21. In-memory jobs are not guaranteed to survive process termination.
22. Future durable transports must preserve the envelope semantics.

## 35. Design Definition of Done

- [x] Envelope schema and invariants.
- [x] State machine and transitions.
- [x] Bounded queue semantics.
- [x] Queue backpressure behavior.
- [x] Retry classification and escape hatch.
- [x] Exponential retry backoff.
- [x] Delayed retry scheduler.
- [x] Scheduler synchronization model.
- [x] Retry scheduler capacity.
- [x] Main queue starvation behavior.
- [x] Cancellation semantics.
- [x] Idempotency contract.
- [x] Graceful shutdown behavior.
- [x] Observability and metrics requirements.
- [x] Trace correlation.
- [x] DI validation.
- [x] Failure states.
- [x] Non-goals.
- [x] Deferred questions.
- [x] Contract signatures.
- [x] Decision traceability.
- [x] Design invariants.
- [x] Proposed MVP configuration defaults.
- [x] Retry inbox full behavior.
- [x] Attempt initial value and execution-start increment semantics.
- [x] JobType stability invariant.
- [x] IdempotencyKey validation contract.

**Design status: Approved for implementation.**


