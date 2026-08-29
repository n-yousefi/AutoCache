# Release validation — 2026-08-29

## Status

The confirmed review defects are fixed. Local release verification passed on Windows for .NET 8.0.29 and .NET 10.0.10 using SDK 10.0.302. A NuGet package was built and consumed successfully by an independent application on both runtimes.

The release version is **4.0.0**. The publication preflight built both the primary and symbol packages and restored the primary package into independent .NET 8 and .NET 10 consumers. This evidence supports publishing the tested stable package; it is not a guarantee of correctness for every backend or a substitute for application-specific staging validation.

## Changes

- Read freshness and hard-expiration timestamps after each awaited storage read, including the flight recheck.
- Queue only the winning flight; stale callers do not execute synchronous factory work or the flight's storage recheck inline.
- Queue source invocation independently and apply one cancellation deadline to queueing/invocation/asynchronous source work. Timed-out results cannot overwrite a subsequent successful retry.
- Observe each flight once, including a cold flight abandoned by all callers, and observe detached source faults even if they finish in the timeout race.
- Reject NaN/infinite jitter settings.
- Correct the invalid policy-override fixture and supply test cancellation tokens without disabling warnings-as-errors.
- Expand the unit suite from 18 to 48 cases per framework.
- Check actual accessed keys and exact per-key source counts in the load harness, launch real concurrent workers, and add delayed storage, JSON roundtrips, and lifecycle cycles.
- Add `scripts/verify-release.ps1` and a package-consumer smoke program. Configure Windows/Linux CI to use the same script.
- Document instance sharing, request-scoped dependency lifetime, storage deadlines, timeout scheduling limits, invalidation behavior, and backoff-cap behavior.

## Executed verification

Command:

```powershell
./scripts/verify-release.ps1 -TestRepeats 5 -Requests 1000000 -Concurrency 4096 -Keys 10000 -Rounds 100 -StorageRequests 50000
```

| Check | Result |
|---|---|
| Clean Release solution build | Passed, zero warnings/errors |
| Unit suite, .NET 8 | 48/48 passed in each of 5 runs |
| Unit suite, .NET 10 | 48/48 passed in each of 5 runs |
| Total repeated unit-test executions | 480 passed, zero failed/skipped |
| Million-request mixed workload, each runtime | Passed; 4,096 workers, 10,000 actual keys, exactly 10,000 source calls |
| Delayed JSON-storage workload, each runtime | Passed; 50,000 requests, 128 workers, 1,000 actual keys, exactly 1,000 source calls |
| Lifecycle checks | 100 outage/recovery/expiration cycles per workload; 400 total cycles, with no retained flights/backoffs after recovery |
| Original review probes, each runtime | All 3 passed |
| Regression effectiveness against saved original implementation | All 5 selected new cases failed as expected on .NET 8 |
| NuGet pack | Passed |
| Independent consumer restored from the actual nupkg, each runtime | Passed: serialization, cold single-flight, stale refresh, legacy API |
| Whitespace checks on modified existing files | Passed |

The expected failures in `artifacts/regression-baseline.log` come from an isolated project that compiles the saved **original** implementation. It does not replace or modify the fixed production code. They demonstrate that the regressions detect the reviewed defects.

Regression coverage includes both delayed-read expiration paths, exact fresh/stale/expired boundaries, synchronous refresh blocking, synchronous factory timeouts, late successful/faulted results, shared cold failures, waiter cancellation, pre-cancelled calls, storage read/write failures, backoff reclamation, explicit null/default values, JSON envelopes, jitter, and infinite timeout.

## Performance observations

These are synthetic measurements from one Windows workstation, not production capacity guarantees or controlled comparisons between runtime versions.

| Mixed workload | .NET 8 | .NET 10 |
|---|---:|---:|
| 1,000,000 requests, no storage delay | 654,491 req/s | 465,570 req/s |
| p99 for that mixed cold/warm workload | 181.451 ms | 161.214 ms |
| 50,000 requests, delayed JSON storage | 7,957 req/s | 8,559 req/s |
| p99 with delayed JSON storage | 53.311 ms | 61.453 ms |

The mixed workloads include initial population. In-memory runs lasted 1.53 s and 2.15 s; delayed-storage runs lasted 6.28 s and 5.84 s. The latter uses `Task.Delay(1)` to simulate storage latency, subject to Windows timer/scheduling behavior. Lifecycle time is advanced explicitly; 100 cycles do not represent a long-duration wall-clock soak.

Do not directly compare these rates with the original review's harness: the new harness accesses all configured keys, checks exact counts, and schedules concurrent workers. The earlier selector accessed only 36% of configured keys in the reported workloads.

## Package

- Files: `artifacts/packages/AutoCache.4.0.0.nupkg` and `AutoCache.4.0.0.snupkg`
- Targets: `net8.0` and `net10.0`
- Includes: both assemblies, XML documentation, and updated README
- Runtime NuGet dependencies: none, confirmed in the nuspec
- SHA-256 sidecar files are regenerated for both artifacts by `scripts/verify-release.ps1`.

The consumer uses an isolated NuGet package cache so an older package with the same version cannot mask the artifact being validated. Do not overwrite an already published package version; select a new version before publishing if this identifier already exists in your feed. The release guard confirmed that `AutoCache` version `4.0.0` returned HTTP 404 from NuGet before publication.

## Remaining production checks

1. Let the configured Windows/Linux CI jobs pass. Local validation here was Windows only; the hosted jobs were not run from this workspace.
2. Exercise the intended production cache adapter and real source dependencies in staging, including serialization, outages, storage timeouts, and sustained expected traffic. No Redis/database adapter or endpoint was supplied in this project.
3. Verify 3.x envelope migration/versioned keys if upgrading existing installations.
4. Reuse a thread-safe adapter instance; ensure factories do not capture request dependencies that will be disposed before background work finishes.
5. Monitor runtime scheduling/thread-pool pressure. No wrapper can forcibly terminate a non-cooperative source or provide hard real-time timeout delivery. Storage operations need their own I/O deadlines.

## Evidence

- `artifacts/release-verification.log`
- `artifacts/release-tests/release-*.trx` (10 result files)
- `artifacts/fixed-probes-net8.log`
- `artifacts/fixed-probes-net10.log`
- `artifacts/regression-baseline.log`
- `artifacts/regression-baseline/results/baseline-regressions.trx`
- `artifacts/pre-fix/` (saved originals for comparison)

This report supersedes the original findings-only `artifacts/PROJECT-REVIEW.md` for the current code state.
