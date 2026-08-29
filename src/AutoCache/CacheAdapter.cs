using System.Collections.Concurrent;
using System.Diagnostics;
using AutoCache.Internal;
using AutoCache.Models;

namespace AutoCache;

/// <summary>
/// Base class for cache stores. AutoCache owns refresh/miss coalescing; derived classes only persist values.
/// </summary>
public abstract class CacheAdapter : ICacheAdapter
{
    private readonly AutoCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxRefreshFailureBackoffEntries;
    private readonly ConcurrentDictionary<FlightKey, Lazy<Task<FactoryExecutionResult>>> _flights = new();
    private readonly ConcurrentDictionary<FlightKey, DateTimeOffset> _refreshRetryAfter = new();
    private readonly object _refreshBackoffMutationGate = new();
    private int _refreshFailureBackoffEntryCount;

    protected CacheAdapter(
        TimeSpan defaultRefreshAt,
        TimeSpan defaultExpiredAt,
        TimeSpan defaultSourceFetchTimeout)
        : this(
            new AutoCacheOptions
            {
                FreshFor = defaultRefreshAt,
                ExpireAfter = defaultExpiredAt,
                FactoryTimeout = defaultSourceFetchTimeout
            },
            TimeProvider.System)
    {
    }

    protected CacheAdapter(AutoCacheOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxRefreshFailureBackoffEntries = options.MaxRefreshFailureBackoffEntries;
    }

    public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);

    public abstract Task<(T Value, bool Found)> GetAsync<T>(string key);

    public abstract Task RemoveAsync(string key);

    /// <summary>
    /// Compatibility overload for AutoCache 3.x. New code should use the CancellationToken-aware overload.
    /// </summary>
    public Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<(T Value, bool HasValue)>> resourceFetch,
        TimeSpan? refreshAt = null,
        TimeSpan? expireAt = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(resourceFetch);

        var policy = new AutoCachePolicy
        {
            FreshFor = refreshAt,
            ExpireAfter = expireAt,
            FactoryTimeout = timeout
        };

        return GetOrCreateAsync(
            key,
            async _ =>
            {
                var result = await resourceFetch().ConfigureAwait(false);
                return new AutoCacheFactoryResult<T>(result.Value, result.HasValue);
            },
            policy,
            CancellationToken.None);
    }

    /// <summary>
    /// Gets a fresh/stale value or invokes the source factory using single-flight coalescing.
    /// Stale values are returned immediately while exactly one background refresh runs per key and value type.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<AutoCacheFactoryResult<T>>> resourceFetch,
        AutoCachePolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key cannot be null or whitespace.", nameof(key));

        ArgumentNullException.ThrowIfNull(resourceFetch);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedPolicy = ResolvePolicy(policy);
        var (cached, found) = await GetAsync<CacheValue<T>>(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        if (found && cached is not null)
        {
            if (cached.IsFresh(now))
            {
                ClearRefreshBackoff<T>(key);
                AutoCacheMetrics.FreshHit();
                return cached.Value;
            }

            if (cached.IsStale(now))
            {
                AutoCacheMetrics.StaleHit();
                if (!IsRefreshBackedOff<T>(key, now))
                    StartBackgroundRefresh(key, resourceFetch, resolvedPolicy);
                else
                    AutoCacheMetrics.RefreshSuppressed();

                return cached.Value;
            }

            // Never serve an expired envelope. Do not delete it here: multiple callers can observe
            // the same expired value, and a late RemoveAsync from one caller could race with the
            // winning factory and delete the freshly written replacement. The single-flight factory
            // overwrites the envelope on success; adapters may also evict it using the hard TTL.
            ClearRefreshBackoff<T>(key);
        }

        AutoCacheMetrics.Miss();
        var flight = GetOrCreateFlight(key, resourceFetch, resolvedPolicy, background: false);
        var result = await flight.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!result.HasValue)
            throw new AutoCacheSourceUnavailableException(key);

        return (T)result.Value!;
    }

    private void StartBackgroundRefresh<T>(
        string key,
        Func<CancellationToken, ValueTask<AutoCacheFactoryResult<T>>> resourceFetch,
        ResolvedPolicy policy)
    {
        _ = GetOrCreateFlight(key, resourceFetch, policy, background: true);
    }

    private static async Task ObserveFlightAsync(Task<FactoryExecutionResult> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Observe once per flight, including cold flights whose callers all cancel.
            // Awaiting callers still receive the original error; diagnostics are recorded below.
        }
    }

    private Task<FactoryExecutionResult> GetOrCreateFlight<T>(
        string key,
        Func<CancellationToken, ValueTask<AutoCacheFactoryResult<T>>> resourceFetch,
        ResolvedPolicy policy,
        bool background)
    {
        var flightKey = new FlightKey(key, typeof(T));
        Lazy<Task<FactoryExecutionResult>> candidate = null!;

        candidate = new Lazy<Task<FactoryExecutionResult>>(
            () =>
            {
                // Never run backend or factory code inside Lazy initialization on the caller.
                // Only the winning Lazy queues work, so a stale storm still queues one flight.
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return await ExecuteFactoryAndStoreAsync(key, resourceFetch, policy, background).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (_flights.TryGetValue(flightKey, out var current) && ReferenceEquals(current, candidate))
                            _flights.TryRemove(flightKey, out _);
                    }
                });
                _ = ObserveFlightAsync(task);
                return task;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        var winner = _flights.GetOrAdd(flightKey, candidate);
        return winner.Value;
    }

    private async Task<FactoryExecutionResult> ExecuteFactoryAndStoreAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<AutoCacheFactoryResult<T>>> resourceFetch,
        ResolvedPolicy policy,
        bool background)
    {
        // A flight can complete and remove itself while another request still holds an older stale
        // envelope. Re-checking here closes that tiny race without retaining completed locks.
        var (latest, latestFound) = await GetAsync<CacheValue<T>>(key).ConfigureAwait(false);
        var checkTime = _timeProvider.GetUtcNow();
        if (latestFound && latest is not null && latest.IsFresh(checkTime))
        {
            ClearRefreshBackoff<T>(key);
            return new FactoryExecutionResult(latest.Value, true);
        }

        // A stale caller can observe an expired retry-backoff just before a previous fast-failing
        // refresh establishes the next backoff. If that previous flight then removes itself first,
        // the caller may create a new flight. Re-check at the factory boundary to close that race.
        if (background && latestFound && latest is not null && latest.IsStale(checkTime) &&
            IsRefreshBackedOff<T>(key, checkTime))
        {
            AutoCacheMetrics.RefreshSuppressed();
            return new FactoryExecutionResult(latest.Value, true);
        }

        AutoCacheMetrics.FactoryCall();
        using var activity = AutoCacheMetrics.StartFactoryActivity(background);
        var started = Stopwatch.GetTimestamp();
        CancellationTokenSource? timeoutCts = null;
        Task<AutoCacheFactoryResult<T>>? sourceTask = null;

        try
        {
            timeoutCts = CreateFactoryTimeoutTokenSource(policy.FactoryTimeout);
            var sourceToken = timeoutCts?.Token ?? CancellationToken.None;
            // The timeout must cover invocation itself, including synchronous work before
            // the factory returns a ValueTask. Use one deadline for queueing and execution.
            sourceTask = Task.Run(async () =>
                await resourceFetch(sourceToken).ConfigureAwait(false), sourceToken);
            var sourceResult = await sourceTask.WaitAsync(sourceToken).ConfigureAwait(false);
            sourceToken.ThrowIfCancellationRequested();

            if (!sourceResult.HasValue)
            {
                if (background)
                    MarkRefreshFailure<T>(key, policy.RefreshFailureBackoff);

                return new FactoryExecutionResult(null, false);
            }

            var now = _timeProvider.GetUtcNow();
            var freshFor = ApplyRefreshJitter(policy.FreshFor, policy.RefreshJitterRatio);
            var entry = new CacheValue<T>
            {
                Value = sourceResult.Value,
                CreatedAt = now,
                RefreshAt = now + freshFor,
                ExpiresAt = now + policy.ExpireAfter
            };

            await SetAsync(key, entry, policy.ExpireAfter).ConfigureAwait(false);
            ClearRefreshBackoff<T>(key);
            return new FactoryExecutionResult(sourceResult.Value, true);
        }
        catch (TimeoutException ex)
        {
            ObserveDetachedSourceIfNeeded(sourceTask);

            if (background)
                MarkRefreshFailure<T>(key, policy.RefreshFailureBackoff);

            AutoCacheMetrics.FactoryFailure();
            activity?.SetStatus(ActivityStatusCode.Error, "factory timeout");
            throw new TimeoutException($"The source factory for cache key '{key}' exceeded {policy.FactoryTimeout}.", ex);
        }
        catch (OperationCanceledException ex) when (timeoutCts?.IsCancellationRequested == true)
        {
            ObserveDetachedSourceIfNeeded(sourceTask);

            if (background)
                MarkRefreshFailure<T>(key, policy.RefreshFailureBackoff);

            AutoCacheMetrics.FactoryFailure();
            activity?.SetStatus(ActivityStatusCode.Error, "factory timeout");
            throw new TimeoutException($"The source factory for cache key '{key}' exceeded {policy.FactoryTimeout}.", ex);
        }
        catch (Exception ex)
        {
            if (background)
                MarkRefreshFailure<T>(key, policy.RefreshFailureBackoff);

            AutoCacheMetrics.FactoryFailure();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
            AutoCacheMetrics.RecordFactoryDuration(Stopwatch.GetElapsedTime(started));
        }
    }


    private static void ObserveDetachedSourceIfNeeded<T>(Task<AutoCacheFactoryResult<T>>? sourceTask)
    {
        if (sourceTask is null)
            return;

        _ = ObserveDetachedSourceAsync(sourceTask);
    }

    private static async Task ObserveDetachedSourceAsync<T>(Task<T> sourceTask)
    {
        try
        {
            await sourceTask.ConfigureAwait(false);
        }
        catch
        {
            // The public operation already timed out. This observation only prevents a late fault
            // from becoming an unobserved task exception when a non-cooperative source ignores cancellation.
        }
    }

    private CancellationTokenSource? CreateFactoryTimeoutTokenSource(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
            return null;

        // The token is handed to the source factory, so cooperative factories are genuinely cancelled.
        // The caller's cancellation token intentionally does not cancel the shared flight: one caller
        // abandoning a request must not abort work that other coalesced callers are waiting for.
        return new CancellationTokenSource(timeout, _timeProvider);
    }

    private ResolvedPolicy ResolvePolicy(AutoCachePolicy? policy)
    {
        var freshFor = policy?.FreshFor ?? _options.FreshFor;
        var expireAfter = policy?.ExpireAfter ?? _options.ExpireAfter;
        var timeout = policy?.FactoryTimeout ?? _options.FactoryTimeout;
        var failureBackoff = policy?.RefreshFailureBackoff ?? _options.RefreshFailureBackoff;
        var jitter = policy?.RefreshJitterRatio ?? _options.RefreshJitterRatio;

        AutoCacheOptions.ValidateDurations(freshFor, expireAfter, timeout, failureBackoff, jitter);
        return new ResolvedPolicy(freshFor, expireAfter, timeout, failureBackoff, jitter);
    }


    private bool IsRefreshBackedOff<T>(string key, DateTimeOffset now)
    {
        var flightKey = new FlightKey(key, typeof(T));
        if (!_refreshRetryAfter.TryGetValue(flightKey, out var retryAfter))
            return false;

        return now < retryAfter;
    }

    private void MarkRefreshFailure<T>(string key, TimeSpan backoff)
    {
        if (backoff <= TimeSpan.Zero)
            return;

        var flightKey = new FlightKey(key, typeof(T));
        var now = _timeProvider.GetUtcNow();
        var retryAfter = now + backoff;

        // Mutations are rare (only refresh success/failure paths). Serializing failure mutations
        // lets scavenging enforce a strict cardinality cap without racing a concurrent extension
        // of the same backoff entry. Reads remain lock-free on the hot stale-hit path.
        lock (_refreshBackoffMutationGate)
        {
            while (true)
            {
                if (_refreshRetryAfter.TryGetValue(flightKey, out var existing))
                {
                    if (_refreshRetryAfter.TryUpdate(flightKey, retryAfter, existing))
                        return;

                    continue;
                }

                if (!TryReserveRefreshBackoffSlot())
                {
                    ScavengeExpiredRefreshBackoffs(now, maxToRemove: 256);
                    if (!TryReserveRefreshBackoffSlot())
                        return;
                }

                if (_refreshRetryAfter.TryAdd(flightKey, retryAfter))
                    return;

                // The add can only lose to a concurrent clear/add edge because failure mutations
                // are serialized. Return the reservation before retrying.
                Interlocked.Decrement(ref _refreshFailureBackoffEntryCount);
            }
        }
    }

    private bool TryReserveRefreshBackoffSlot()
    {
        while (true)
        {
            var count = Volatile.Read(ref _refreshFailureBackoffEntryCount);
            if (count >= _maxRefreshFailureBackoffEntries)
                return false;

            if (Interlocked.CompareExchange(ref _refreshFailureBackoffEntryCount, count + 1, count) == count)
                return true;
        }
    }

    private void ScavengeExpiredRefreshBackoffs(DateTimeOffset now, int maxToRemove)
    {
        var removed = 0;
        foreach (var pair in _refreshRetryAfter)
        {
            if (pair.Value > now)
                continue;

            if (TryRemoveRefreshBackoff(pair.Key))
                removed++;

            if (removed >= maxToRemove)
                break;
        }
    }

    private void ClearRefreshBackoff<T>(string key) =>
        TryRemoveRefreshBackoff(new FlightKey(key, typeof(T)));

    private bool TryRemoveRefreshBackoff(FlightKey key)
    {
        if (!_refreshRetryAfter.TryRemove(key, out _))
            return false;

        Interlocked.Decrement(ref _refreshFailureBackoffEntryCount);
        return true;
    }

    private static TimeSpan ApplyRefreshJitter(TimeSpan freshFor, double ratio)
    {
        if (ratio <= 0)
            return freshFor;

        var reduction = Random.Shared.NextDouble() * ratio;
        return TimeSpan.FromTicks((long)(freshFor.Ticks * (1d - reduction)));
    }

    internal int InFlightCount => _flights.Count;
    internal int RefreshFailureBackoffCount => Volatile.Read(ref _refreshFailureBackoffEntryCount);

    internal async Task WaitForIdleAsync()
    {
        while (true)
        {
            var tasks = _flights.Values
                .Where(static lazy => lazy.IsValueCreated)
                .Select(static lazy => lazy.Value)
                .ToArray();

            if (tasks.Length == 0)
                return;

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // Tests use this helper to wait for cleanup even after deliberately failing refreshes.
            }

            if (_flights.IsEmpty)
                return;
        }
    }
}
