# Changelog

## 4.0.0 - 2026-08-29

- Recheck freshness and hard expiration after asynchronous storage reads.
- Queue one shared flight so stale callers never execute synchronous refresh work inline.
- Include synchronous factory execution in the timeout boundary; discard late results and observe late faults.
- Observe abandoned cold flights when all request waiters cancel.
- Reject non-finite jitter settings.
- Fix the policy-override test fixture and xUnit cancellation diagnostics without relaxing warnings as errors.
- Expand regression coverage for storage failures, cancellation, expiration boundaries, serialization, and timeout races.
- Validate exact per-key load invariants on both frameworks and repeat outage/recovery/expiration cycles.
- Add a repeatable clean-build, load, packaging, and package-consumer verification script for Windows/Linux CI.
- Rebuilt miss and refresh concurrency around removable per-key single-flight tasks.
- Fixed stale-refresh stampedes.
- Preserve stale data on failed/empty background refresh.
- Added bounded per-key background-refresh failure backoff.
- Added token-aware source factory and cooperative factory timeout.
- Added caller cancellation that does not abort shared work.
- Added `TimeProvider` timestamps and refresh jitter.
- Added Metrics and ActivitySource instrumentation.
- Retained the AutoCache 3.x adapter and GetOrCreateAsync calling shape as a migration bridge.
- Added concurrency-focused unit tests and a standalone heavy-load harness.
