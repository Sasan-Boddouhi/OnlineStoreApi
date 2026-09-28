# Background Processing

## Purpose

Background Processing provides an in-process, bounded execution pipeline for application jobs without coupling Application contracts to the transport implementation.

## Architecture

```
Application
    |
IBackgroundJobQueue
    |
Infrastructure bounded Channel
    |
BackgroundJobWorker
    |
BackgroundJobDispatcher
    |
IBackgroundJobHandler<TJob>
```

Infrastructure also owns retry classification and delayed retry scheduling.

## Configuration

The MVP defaults are:

- Main queue capacity: 1000
- Enqueue timeout: 5 seconds
- Maximum IdempotencyKey length: 256
- Retry inbox capacity: 100
- Retry enqueue timeout: 5 seconds
- Retry base delay: 1 second
- Retry maximum delay: 300 seconds
- Maximum execution attempts: 5
- Maximum requeue attempts: 10
- Job timeout: 60 seconds

## Where to extend

- Add application jobs and handlers under the Application layer.
- Register handlers through the Infrastructure background-job registration extensions.
- Keep queue/worker/retry implementation details inside Infrastructure.
- Do not move Channel, BackgroundService, retry scheduler, or transport-specific types into Application.
- A future external broker can replace the Infrastructure transport without changing Application queue contracts.

## Testing

Unit tests live under `OnlineStore.Tests.Unit/BackgroundJobs/`. Timing-sensitive tests use `FakeTimeProvider`.

The complete failure-matrix mapping is maintained in:

`docs/specs/background-processing-fp-mapping.md`

That mapping is intentionally evidence-based and identifies any remaining test gaps instead of treating incidental coverage as dedicated coverage.
