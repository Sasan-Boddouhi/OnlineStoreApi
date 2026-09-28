# Background Processing — Failure Matrix Mapping

**Status:** M2.6 review artifact  
**Scope:** FP-F01 through FP-F38

This table maps every failure-matrix identifier to the concrete test method currently present in the repository. A row marked **Gap** is intentionally not represented by a dedicated test method yet; it must not be interpreted as covered merely because adjacent code paths are exercised.

| ID | Test method / evidence | Status |
|---|---|---|
| FP-F01 | `ChannelBackgroundJobQueueTests.EnqueueAsync_MainQueueAtCapacity_ReturnsQueueFull` | Covered |
| FP-F02 | No dedicated test for caller cancellation while waiting on a full queue | Gap |
| FP-F03 | `ChannelBackgroundJobQueueTests.EnqueueAsync_AfterShutdown_ReturnsShuttingDown` | Covered |
| FP-F04 | `ChannelBackgroundJobQueueTests.EnqueueAsync_PayloadFromDisposedDocument_PayloadRemainsReadable` | Covered |
| FP-F05 | `StaticRetryPolicyTests.Evaluate_UnknownException_ReturnsDoNotRetry` | Covered |
| FP-F06 | `StaticRetryPolicyTests.Evaluate_NonRetryableException_ReturnsDoNotRetry` | Covered |
| FP-F07 | `StaticRetryPolicyTests.Evaluate_HttpRequestExceptionBeforeLimit_ReturnsRetry` | Covered |
| FP-F08 | `StaticRetryPolicyTests.Evaluate_ExecutionAttemptAtLimit_ReturnsDoNotRetry` | Covered |
| FP-F09 | No dedicated test for consecutive worker backoff progression and cap | Gap |
| FP-F10 | `RetrySchedulerTests.ScheduleAsync_MultipleRetries_UsesDueAtOrdering` | Covered |
| FP-F11 | No dedicated test proving an earlier newly submitted retry interrupts an existing later wait | Gap |
| FP-F12 | No dedicated retry-inbox capacity/timeout test | Gap |
| FP-F13 | No dedicated main-queue-full-at-retry-due test | Gap |
| FP-F14 | No dedicated RetryQueueStarvation test | Gap |
| FP-F15 | No dedicated test proving Attempt remains unchanged when requeue fails | Gap |
| FP-F16 | `ChannelBackgroundJobQueueTests.EnqueueAsync_NewlyEnqueuedEnvelope_AttemptIsZero` + `BackgroundJobDispatcherTests.DispatchAsync_RegisteredHandler_ExecutesJob` | Covered |
| FP-F17 | No dedicated end-to-end retry-success test | Gap |
| FP-F18 | No dedicated timeout-with-retry-policy test | Gap |
| FP-F19 | No dedicated terminal JobTimeout cancellation test | Gap |
| FP-F20 | No dedicated scheduler-backoff shutdown interruption test | Gap |
| FP-F21 | No dedicated worker-wait shutdown test | Gap |
| FP-F22 | No dedicated graceful-drain integration test | Gap |
| FP-F23 | No dedicated scoped-handler isolation test | Gap |
| FP-F24 | `BackgroundJobDispatcherTests.DispatchAsync_MissingHandler_ThrowsInvalidOperationException` | Partially covered |
| FP-F25 | No dedicated duplicate-handler startup validation test | Gap |
| FP-F26 | No dedicated Activity correlation/trace propagation test | Gap |
| FP-F27 | No dedicated success observability assertion | Gap |
| FP-F28 | No dedicated retry observability assertion | Gap |
| FP-F29 | No dedicated cancellation observability assertion | Gap |
| FP-F30 | No dedicated concurrent-producer determinism test | Gap |
| FP-F31 | No dedicated retry idempotency propagation test | Gap |
| FP-F32 | `StaticRetryPolicyTests.Evaluate_ExecutionAttemptAtLimit_ReturnsDoNotRetry` | Covered |
| FP-F33 | No dedicated scheduler shutdown with pending-item test | Gap |
| FP-F34 | `ChannelBackgroundJobQueueTests.EnqueueAsync_RegisteredJob_JobTypeIsNotAssemblyQualified` | Covered |
| FP-F35 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyExceedsMaxLength_ThrowsArgumentOutOfRange` | Covered |
| FP-F36 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyWhitespace_ThrowsArgumentException` | Covered |
| FP-F37 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyNull_ThrowsArgumentNullException` | Covered |
| FP-F38 | No dedicated retry-inbox starvation test proving the terminal cancellation path | Gap |

## Review conclusion

The repository's 347 passing tests do **not** constitute one-to-one coverage of FP-F01..FP-F38. The mapping above is deliberately evidence-based and exposes the remaining gaps instead of treating incidental coverage as proof.

The existing implementation therefore has a test-matrix coverage gap even though the solution build and current test suite pass.
