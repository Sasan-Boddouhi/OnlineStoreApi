# Background Processing — Failure Matrix Mapping

**Status:** M2.7 closure artifact  
**Scope:** FP-F01 through FP-F38

This table maps every failure-matrix identifier to the concrete test method currently present in the repository. A row marked **Gap** is intentionally not represented by a dedicated test method yet; it must not be interpreted as covered merely because adjacent code paths are exercised.

| ID | Test method / evidence | Status |
|---|---|---|
| FP-F01 | `ChannelBackgroundJobQueueTests.EnqueueAsync_MainQueueAtCapacity_ReturnsQueueFull` | Covered |
| FP-F02 | `BackgroundJobFailureMatrixTests.EnqueueAsync_CallerCancellationWhileQueueIsFull_ThrowsOperationCanceledException` | Covered |
| FP-F03 | `ChannelBackgroundJobQueueTests.EnqueueAsync_AfterShutdown_ReturnsShuttingDown` | Covered |
| FP-F04 | `ChannelBackgroundJobQueueTests.EnqueueAsync_PayloadFromDisposedDocument_PayloadRemainsReadable` | Covered |
| FP-F05 | `StaticRetryPolicyTests.Evaluate_UnknownException_ReturnsDoNotRetry` | Covered |
| FP-F06 | `StaticRetryPolicyTests.Evaluate_NonRetryableException_ReturnsDoNotRetry` | Covered |
| FP-F07 | `StaticRetryPolicyTests.Evaluate_HttpRequestExceptionBeforeLimit_ReturnsRetry` | Covered |
| FP-F08 | `StaticRetryPolicyTests.Evaluate_ExecutionAttemptAtLimit_ReturnsDoNotRetry` | Covered |
| FP-F09 | `BackgroundJobFailureMatrixTests.BackgroundJobWorker_GetRetryDelay_UsesExponentialProgressionAndCap` | Covered |
| FP-F10 | `RetrySchedulerTests.ScheduleAsync_MultipleRetries_UsesDueAtOrdering` | Covered |
| FP-F11 | `BackgroundJobFailureMatrixTests.ScheduleAsync_EarlierRetryArrivesDuringLaterWait_WakesSchedulerEarly` | Covered |
| FP-F12 | `BackgroundJobFailureMatrixTests.ScheduleAsync_RetryInboxAtCapacity_TimeoutReturnsFalseWithoutUnboundedGrowth` | Covered |
| FP-F13 | `BackgroundJobFailureMatrixTests.RetryScheduler_MainQueueFullAtDueTime_RequeuesAfterPressureIsReleased` | Covered |
| FP-F14 | `BackgroundJobFailureMatrixTests.RetryScheduler_RequeueStarvation_ReachesTerminalCancellation` | Covered |
| FP-F15 | `BackgroundJobFailureMatrixTests.RequeueAsync_MainQueueFull_PreservesEnvelopeAttempt` | Covered |
| FP-F16 | `ChannelBackgroundJobQueueTests.EnqueueAsync_NewlyEnqueuedEnvelope_AttemptIsZero` + `BackgroundJobDispatcherTests.DispatchAsync_RegisteredHandler_ExecutesJob` | Covered |
| FP-F17 | `BackgroundJobFailureMatrixTests.Dispatcher_RetryEnvelopeAfterTransientFailure_ExecutesSuccessfullyWithStableIdentity` | Covered |
| FP-F18 | `BackgroundJobFailureMatrixTests.StaticRetryPolicy_TimeoutBeforeMaximumAttempts_ReturnsRetry` | Covered |
| FP-F19 | `BackgroundJobFailureMatrixTests.DispatchAsync_TimeoutCancellation_EmitsCancellationReason` | Covered |
| FP-F20 | `BackgroundJobFailureMatrixTests.RetryScheduler_ShutdownDuringBackoff_InterruptsWaitAndDoesNotStartRetry` | Covered |
| FP-F21 | `BackgroundProcessingHostedServiceTests.Worker_HostShutdownWhileWaiting_StopsPromptly` | Covered |
| FP-F22 | `BackgroundProcessingHostedServiceTests.Worker_GracefulShutdown_DrainsAcceptedWorkBeforeStopCompletes` | Covered |
| FP-F23 | `BackgroundProcessingHostedServiceTests.Dispatcher_ScopedHandler_CreatesSeparateScopePerExecution` | Covered |
| FP-F24 | `BackgroundProcessingHostedServiceTests.RegistrationValidator_NoHandler_FailsApplicationStartup` + `BackgroundJobDispatcherTests.DispatchAsync_MissingHandler_ThrowsInvalidOperationException` | Covered |
| FP-F25 | `BackgroundProcessingHostedServiceTests.RegistrationValidator_DuplicateHandler_FailsApplicationStartup` | Covered |
| FP-F26 | `BackgroundJobFailureMatrixTests.Dispatcher_TraceAndCorrelationContext_PropagatesToHandler` | Covered |
| FP-F27 | `BackgroundJobFailureMatrixTests.Dispatcher_Success_EmitsStartedAndSucceededObservabilityEvents` | Covered |
| FP-F28 | `BackgroundJobFailureMatrixTests.RetryScenario_EmitsRetryScheduledEventAndRetryMetrics` | Covered |
| FP-F29 | `BackgroundJobFailureMatrixTests.Dispatcher_TimeoutCancellation_EmitsCancellationReason` | Covered |
| FP-F30 | `BackgroundJobFailureMatrixTests.EnqueueAsync_ConcurrentProducers_AcceptAllCoordinatedWrites` | Covered |
| FP-F31 | `BackgroundJobFailureMatrixTests.Dispatcher_RetryEnvelopeAfterTransientFailure_ExecutesSuccessfullyWithStableIdentity` | Covered |
| FP-F32 | `StaticRetryPolicyTests.Evaluate_ExecutionAttemptAtLimit_ReturnsDoNotRetry` | Covered |
| FP-F33 | `BackgroundJobFailureMatrixTests.RetryScheduler_ShutdownWithPendingItems_DoesNotStartPendingRetry` | Covered |
| FP-F34 | `ChannelBackgroundJobQueueTests.EnqueueAsync_RegisteredJob_JobTypeIsNotAssemblyQualified` | Covered |
| FP-F35 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyExceedsMaxLength_ThrowsArgumentOutOfRange` | Covered |
| FP-F36 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyWhitespace_ThrowsArgumentException` | Covered |
| FP-F37 | `ChannelBackgroundJobQueueTests.EnqueueAsync_IdempotencyKeyNull_ThrowsArgumentNullException` | Covered |
| FP-F38 | `BackgroundJobFailureMatrixTests.RetryScheduler_RetryInboxStarvation_ReturnsFalseWithinConfiguredTimeout` | Covered |

## Review conclusion

M2.7 closes the remaining dedicated observability and shutdown coverage obligations. FP-F20 and FP-F28 are covered by explicit tests, and Health Checks are covered by dedicated integration tests.