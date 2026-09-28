# Background Processing — Testing Strategy

**Status:** Approved Approach — Document Pending Review  
**Document:** `docs/specs/background-processing-testing-strategy.md`  
**Target Framework:** .NET 8 / ASP.NET Core 8  
**Scope:** Tests for the in-process background job processing subsystem

## 1. Purpose

This document defines the testing strategy for Background Processing. It translates the approved Design Specification into deterministic, maintainable test levels and a concrete failure-oriented test catalog.

The strategy prioritizes lifecycle correctness, bounded resource behavior, retry semantics, cancellation, scheduler behavior, DI validation, and graceful shutdown over happy-path coverage alone.

The strategy does not introduce a new test project and does not require a global repository coverage threshold.

## 2. Test Pyramid & Level Definitions

The target distribution is a guideline, not a coverage gate:

| Level | Target Share | Purpose |
|---|---:|---|
| Unit | ~70% | Fast, deterministic verification of queue, worker, dispatcher, retry policy, scheduler, lifecycle, and validation logic |
| Integration | ~25% | Verify real DI composition, hosted services, bounded channels, scheduler/worker interaction, scoped handlers, and shutdown behavior |
| API | ~5% | Verify HTTP-to-queue behavior and externally visible status mapping |

No separate E2E test project is required for this milestone.

The pyramid is intentionally approximate. Test placement is determined by the behavior being verified, not by forcing an exact percentage.

## 3. Test Doubles (TimeProvider, Fakes)

### 3.1 Time abstraction

Time-sensitive infrastructure must depend on `TimeProvider`, not directly on wall-clock APIs.

Unit tests use `FakeTimeProvider` so retry due times and backoff progression can be advanced without real waiting.

### 3.2 Scheduler waiting

The Retry Scheduler must use a wait mechanism that is driven by `TimeProvider`. Acceptable implementation patterns are:

- `TimeProvider`-aware delay/wait abstraction.
- A timer abstraction created from `TimeProvider`.

A raw `Task.Delay(delay, cancellationToken)` that bypasses `TimeProvider` is not acceptable for scheduler timing because `FakeTimeProvider.Advance()` would not deterministically wake the scheduler.

ASP.NET Core hosted services are long-running `BackgroundService` implementations and are expected to honor cancellation during graceful shutdown. citeturn0search1

### 3.3 Other test doubles

Use focused fakes/mocks for:

- Job handlers.
- Retry policy.
- Queue boundary where testing scheduler/worker independently is useful.
- Clock/time provider.
- Logger/observability sink where event assertions are required.
- DI service registrations where invalid-registration scenarios must be constructed intentionally.

Tests must not replace the component under test with a mock of the same behavior.

## 4. Determinism Rules

1. No real waiting for retry/backoff tests.
2. No arbitrary `Task.Delay` used as a test synchronization mechanism.
3. Use `FakeTimeProvider` for time-based behavior.
4. Use `CancellationTokenSource.Cancel()` for event-based shutdown/cancellation.
5. MVP retry jitter is disabled, so retry timing is deterministic.
6. Concurrent tests must synchronize explicitly rather than relying on scheduler timing.
7. Tests must observe explicit state/events/completion signals rather than sleep-and-poll loops.
8. Each test owns its queue, scheduler, worker, DI scope, and mutable state unless the test explicitly verifies shared infrastructure.

### Attempt invariant

`Attempt` is incremented at actual execution start, in Worker/Dispatcher, immediately before handler invocation.

It must not be incremented by the Retry Scheduler or by queue enqueue/requeue operations.

This preserves the invariant that `Attempt` equals the number of execution attempts that actually started, including the case where a retry cannot be re-enqueued because the main queue is full.

## 5. Test Project Structure

Use the existing test projects:

- `OnlineStore.Tests.Unit`
- `OnlineStore.Tests.Integration`
- `OnlineStore.Tests.Shared`

No new test project is required.

Suggested organization:

```text
OnlineStore.Tests.Unit
  BackgroundProcessing
    Queue
    Retry
    Scheduler
    Worker
    Dispatcher
    Validation

OnlineStore.Tests.Integration
  BackgroundProcessing
    Queue
    HostedServices
    Retry
    Shutdown
    DependencyInjection

OnlineStore.Tests.Shared
  BackgroundProcessing
    Fixtures
    TestJobs
    TestHandlers
    Assertions
```

The exact folder layout may follow existing repository conventions if they differ during implementation.

## 6. Naming Convention

Use:

```text
MethodName_Scenario_ExpectedOutcome
```

Examples:

```csharp
EnqueueAsync_QueueFull_ReturnsQueueFullResult()
HandleAsync_UnknownException_DoesNotRetry()
Scheduler_ShutdownDuringBackoff_CancelsRetry()
Dispatcher_HandlerThrowsRetryableException_SchedulesRetry()
Worker_ExecutionStarts_IncrementsAttempt()
```

Names should describe observable behavior, not implementation details.

## 7. Test Isolation & Parallelism

### Unit tests

Background-processing unit tests that require shared mutable state within a scenario should be grouped with xUnit `[Collection]` where appropriate.

Concurrency tests must still use independent fixtures and state per test.

Concurrent producer tests use synchronization primitives such as `Barrier` or `CountdownEvent` to make the start/coordination deterministic.

### Integration tests

Integration tests use independent application/test-host contexts and their own queue, scheduler, handler registrations, and mutable state.

They must not rely on state left by another test.

Integration tests therefore follow independent factory/context isolation rather than globally disabling xUnit parallelism.

The strategy does not disable repository-wide test parallelism merely to accommodate this feature.

## 8. Coverage Strategy (risk-based)

Coverage is risk-based and applies most strongly to core background infrastructure.

Targets:

- Core queue, worker, dispatcher, retry, scheduler, cancellation, and validation logic: approximately 90% line coverage target.
- Decision-heavy logic: meaningful branch coverage is required, especially retry classification, retry exhaustion, timeout classification, shutdown, queue-full behavior, and starvation.
- API mapping: cover all externally meaningful queue result mappings.
- No arbitrary global repository coverage gate is introduced.

Coverage is evidence of exercised behavior, not proof of correctness. A high line percentage does not replace the failure matrix.

## 9. CI Integration

All Background Processing tests must execute in the existing CI pipeline.

Requirements:

- Unit tests run on every CI test execution.
- Integration tests run on every CI test execution.
- API tests run on every CI test execution.
- Coverage collection remains part of the existing test/coverage workflow.
- No test may require a developer-only local service unless the existing integration-test infrastructure already provides it.
- No timing-sensitive or sleep-dependent tests are accepted into CI.
- Failures must be reproducible locally using the same test commands used by CI.

## 10. Test Matrix — Unit Level

| Area | Required scenarios |
|---|---|
| Queue enqueue | Accepted, cancellation before enqueue, shutting down, queue-full timeout |
| Queue capacity | Exact capacity accepted; next item rejected after timeout |
| Envelope | Stable JobId, stable IdempotencyKey, payload clone/lifetime safety |
| EnqueueResult | Each status maps to correct `IsAccepted` behavior |
| Retry policy | Retryable exception, non-retryable exception, unknown exception, max-attempt boundary |
| NonRetryableJobException | Explicit permanent classification |
| Backoff | 1s, 2s, 4s progression and 5-minute cap |
| Attempt | Increment exactly once at execution start; not at scheduling/requeue |
| Dispatcher | Correct handler, missing handler, duplicate handler configuration |
| Worker | Success, retry scheduling, permanent failure, terminal timeout, host shutdown |
| Job timeout | Retryable timeout enters retry path; terminal timeout becomes Cancelled with JobTimeout |
| Cancellation | HostShutdown, JobTimeout, RetryQueueStarvation |
| Scheduler ordering | Earliest DueAt executes first |
| Scheduler wake-up | Earlier newly submitted DueAt interrupts current wait |
| Scheduler shutdown | Pending backoff is interrupted; no new retry starts |
| Retry inbox | Capacity limit and producer behavior |
| Main queue pressure | Due retry is held and requeued with bounded backoff |
| Requeue attempts | Limit reached produces RetryQueueStarvation |
| Observability | Required fields/events are emitted for key outcomes |
| DI validation | Zero handlers and multiple handlers fail; exactly one succeeds |

## 11. Test Matrix — Integration Level

| Area | Required scenarios |
|---|---|
| DI composition | Application contracts resolve without Infrastructure leakage |
| Hosted worker | Enqueued job is executed by hosted worker |
| Scoped handler | A fresh DI scope is created per execution |
| Retry flow | Handler failure schedules delayed retry and later executes |
| Retry attempt | Retry execution observes incremented Attempt while JobId/IdempotencyKey remain stable |
| Scheduler/worker | Scheduler submits due retry to the real bounded main queue |
| Queue pressure | Full main queue does not deadlock scheduler |
| Starvation | Requeue limit produces terminal cancellation |
| Startup validation | Invalid handler registrations prevent application startup |
| Graceful shutdown | Accepted work drains within shutdown timeout |
| Shutdown interruption | Scheduler backoff and worker wait observe host cancellation |
| Timeout | Handler timeout follows retry policy and cancellation semantics |
| Trace/correlation | Envelope context reaches handler execution context |
| Observability | Structured lifecycle events are emitted through the real DI pipeline |

Hosted services are activated at application startup and receive cancellation during host shutdown; integration tests must verify the application's actual hosted-service lifecycle rather than only testing worker methods in isolation. citeturn0search1

## 12. Test Matrix — API Level

| Area | Required scenarios |
|---|---|
| Accepted enqueue | API returns success for accepted background job |
| Queue full | API maps `QueueFull` to HTTP 503 |
| Shutting down | API maps `ShuttingDown` to the documented shutdown response |
| Invalid request | Validation failure remains an API concern and does not enter the queue |
| Idempotency | Caller-provided idempotency key is accepted and propagated |
| Correlation | Request correlation/trace metadata is propagated to the queued envelope where available |

API tests must not assert internal queue implementation details such as `Channel<T>`.

## 13. Failure Matrix (Core Test Case Catalog)

Every row below maps to one or more concrete test cases. Test IDs are stable identifiers for the M2.5 implementation phase.

| ID | Failure / Condition | Trigger | Expected Outcome | Required Assertions | Level |
|---|---|---|---|---|---|
| FP-F01 | Main queue at capacity | Enqueue when bounded queue is full | `QueueFull` | Status, no duplicate enqueue, no unbounded growth | Unit + API |
| FP-F02 | Enqueue cancellation | Caller token cancelled while waiting | Enqueue exits as cancellation according to contract | No job accepted after cancellation | Unit |
| FP-F03 | Queue shutting down | Enqueue after shutdown begins | `ShuttingDown` | No new job accepted | Unit + API |
| FP-F04 | Payload lifetime | Envelope payload created from disposable `JsonDocument` | Payload remains readable | Clone survives source document disposal | Unit |
| FP-F05 | Unknown exception | Handler throws unclassified exception | Permanent failure | No retry scheduled; Failed outcome | Unit + Integration |
| FP-F06 | Explicit non-retryable exception | Handler throws `NonRetryableJobException` | Permanent failure | No retry; Failed outcome | Unit + Integration |
| FP-F07 | Retryable exception | Handler throws recognized transient exception | Retry scheduled | RetryScheduled emitted; no blocking worker backoff | Unit + Integration |
| FP-F08 | Retry exhaustion | Retryable failure reaches max execution attempts | Failed | Final failure; no additional retry | Unit + Integration |
| FP-F09 | Backoff progression | Consecutive retryable failures | Exponential delay | 1s/2s/4s progression and cap verified using fake time | Unit |
| FP-F10 | Retry scheduler ordering | Multiple due times | Earliest due item first | Priority ordering and execution order | Unit |
| FP-F11 | Scheduler wake-up | New earlier retry arrives during later wait | Scheduler wakes early | Earlier item executes at its due time | Unit |
| FP-F12 | Retry inbox full | Retry producer submits beyond bounded retry inbox capacity | Backpressure/rejection follows scheduler contract | Capacity remains bounded; no silent loss beyond defined result/handling | Unit |
| FP-F13 | Main queue full at retry due | Scheduler attempts to requeue due retry | Retry held and retried later | Scheduler remains responsive; bounded requeue attempts | Unit + Integration |
| FP-F14 | Retry requeue starvation | Requeue limit exhausted | Cancelled / RetryQueueStarvation | Cancellation reason, no infinite retry loop | Unit + Integration |
| FP-F15 | Attempt increment timing | Retry is scheduled but main queue requeue fails | Attempt unchanged | Attempt increments only immediately before actual handler invocation | Unit |
| FP-F16 | Successful retry | First execution fails transiently; later execution succeeds | Succeeded | Same JobId/IdempotencyKey; Attempt increments exactly once per execution | Integration |
| FP-F17 | Timeout + retryable policy | Handler exceeds JobTimeout and policy permits retry | WaitingForRetry | Not Cancelled; retry scheduled | Unit + Integration |
| FP-F18 | Timeout + terminal policy | Handler exceeds JobTimeout and policy rejects retry | Cancelled / JobTimeout | Terminal cancellation reason is JobTimeout | Unit + Integration |
| FP-F19 | Host shutdown during retry backoff | Host cancellation during delayed retry | Cancelled / shutdown path | Backoff interrupted; no new retry starts | Unit + Integration |
| FP-F20 | Host shutdown while waiting for work | Worker waiting on queue when host stops | Worker exits promptly | No leaked worker task; shutdown completes | Unit + Integration |
| FP-F21 | Graceful drain | Accepted jobs exist during shutdown | Accepted work drains until deadline | Completed jobs remain successful; deadline is honored | Integration |
| FP-F22 | Handler scope isolation | Two executions resolve scoped dependency | Separate scopes | Scoped state is not shared between executions | Integration |
| FP-F23 | Missing handler | Job type has no handler | Startup/configuration failure | Application refuses invalid configuration before traffic | Unit + Integration |
| FP-F24 | Duplicate handler | More than one handler for same job type | Startup/configuration failure | Ambiguity detected before traffic | Unit + Integration |
| FP-F25 | Correlation propagation | Job created inside traced request | Context preserved | CorrelationId/TraceId available in handler context | Unit + Integration |
| FP-F26 | Observability on success | Handler succeeds | Started + Succeeded | Job identity, Attempt, duration and outcome fields present | Unit + Integration |
| FP-F27 | Observability on retry | Handler fails transiently | RetryScheduled + later Started | Attempt and stable identity distinguish retry execution | Unit + Integration |
| FP-F28 | Observability on cancellation | Shutdown/timeout/starvation | Cancelled event | Correct CancellationReason recorded | Unit + Integration |
| FP-F29 | Concurrent producers | Multiple producers enqueue concurrently | No corruption/lost accepted work | Exact accepted count; bounded capacity; deterministic coordination | Unit + Integration |
| FP-F30 | Idempotency propagation | Same caller key across retry | Key remains stable | Every execution observes identical IdempotencyKey | Unit + Integration |
| FP-F31 | Unknown retry decision boundary | Policy receives exception at max attempts | No retry | Boundary condition is deterministic | Unit |
| FP-F32 | Scheduler shutdown with pending items | Scheduler has future retries when host stops | Pending retries do not start | Cancellation observed; pending delayed work remains unstarted | Unit + Integration |

### Test case mapping rule

Each failure-matrix row must map to at least one named test method. Where a behavior crosses component boundaries, the same logical failure may have both a unit and integration test.

The implementation phase must not delete a matrix row merely because another test happens to exercise the same code path. A row can be consolidated only when the resulting test explicitly proves every assertion listed in that row.

## 14. Known Limitations

### 14.1 No jitter in MVP

MVP retry scheduling intentionally has no jitter.

This makes retry behavior deterministic and straightforward to test, but synchronized retries across multiple jobs can produce retry storms under high concurrency.

**Known limitation:** jitter must be reintroduced before any high-concurrency production deployment.

When jitter is introduced, the randomness source must be injectable so retry timing remains testable and bounded.

### 14.2 In-memory durability

Jobs are held in memory. Process termination can lose queued or scheduled work.

This is a deliberate MVP non-goal and is not compensated for by tests.

### 14.3 Single worker

The MVP uses one sequential execution worker. Tests therefore do not establish correctness for multiple worker instances.

### 14.4 No durable idempotency store

The idempotency key is propagated, but the MVP does not provide distributed uniqueness enforcement.

## 15. Deferred Test Concerns

The following are intentionally deferred:

1. Durable queue recovery after process termination.
2. Durable retry recovery.
3. Distributed idempotency race tests.
4. External broker delivery semantics.
5. Multi-node scheduler coordination.
6. Multiple-worker ordering guarantees.
7. Jitter distribution and retry-storm mitigation tests.
8. Dead-letter queue durability and replay tests.
9. Outbox/transactional enqueue consistency.
10. Load/soak testing for sustained high concurrency.
11. Chaos testing for process/node failure.
12. Performance baselines for large payloads and high queue depth.

These concerns must be revisited before introducing the corresponding production capabilities.

## 16. Decision Traceability (D-031..D-037)

| Decision | Status | Testing consequence |
|---|---|---|
| **D-031 — Attempt increments at execution start** | Accepted | Tests must prove scheduling/requeue does not increment Attempt; each actual handler invocation increments it exactly once |
| **D-032 — TimeProvider / FakeTimeProvider** | Accepted | All scheduler/backoff timing tests use TimeProvider; no real-time waits |
| **D-033 — No jitter in MVP; known limitation** | Accepted | Retry delays are deterministic; jitter is a deferred production-hardening concern |
| **D-034 — Risk-based coverage** | Accepted | Core infrastructure targets ~90% line coverage plus meaningful decision/branch coverage; no global gate |
| **D-035 — Deterministic concurrency** | Accepted | Unit concurrency tests use Barrier/CountdownEvent; integration uses independent application contexts |
| **D-036 — Test naming convention** | Accepted | Tests follow MethodName_Scenario_ExpectedOutcome |
| **D-037 — Test isolation strategy** | Accepted | Unit shared-state scenarios use xUnit Collection where needed; integration tests use independent factory/context state |

## Review Checklist

Before M2.4 is closed, the reviewer must confirm:

- [ ] Failure Matrix covers queue pressure, retry, timeout, scheduler, shutdown, starvation, DI validation, observability, idempotency, and concurrency.
- [ ] Every Failure Matrix row maps to at least one named test case.
- [ ] Time-based tests use TimeProvider/FakeTimeProvider.
- [ ] Shutdown tests use cancellation tokens or host StopAsync, not fake time.
- [ ] Attempt semantics match D-031.
- [ ] JobTimeout semantics match the approved N-2 interpretation.
- [ ] RetryDecision remains binary: Retry/DoNotRetry.
- [ ] Dequeue remains an Infrastructure concern.
- [ ] No jitter is treated as a documented limitation.
- [ ] Test isolation does not disable repository-wide parallelism.
- [ ] CI executes all new tests.
- [ ] No test depends on arbitrary sleeps or wall-clock timing.

**M2.4 Testing Strategy: Documented for review.**

