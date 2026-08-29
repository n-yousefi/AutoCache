using AutoCache.Models;
using Xunit;

namespace AutoCache.Tests;

public sealed class GetOrCreateTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static AutoCacheOptions Options(
        TimeSpan? freshFor = null,
        TimeSpan? expireAfter = null,
        TimeSpan? timeout = null,
        int maxRefreshFailureBackoffEntries = 10_000) => new()
    {
        FreshFor = freshFor ?? TimeSpan.FromMinutes(1),
        ExpireAfter = expireAfter ?? TimeSpan.FromMinutes(10),
        FactoryTimeout = timeout ?? TimeSpan.FromSeconds(5),
        RefreshJitterRatio = 0,
        MaxRefreshFailureBackoffEntries = maxRefreshFailureBackoffEntries
    };

    [Fact]
    public async Task Fresh_value_does_not_call_source_again()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var calls = 0;

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            await Task.Yield();
            return AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls));
        }

        Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Cold_stampede_is_coalesced_to_one_source_call()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return AutoCacheFactoryResult<int>.FromValue(42);
        }

        var requests = Enumerable.Range(0, 2_000)
            .Select(_ => cache.GetOrCreateAsync("same-key", Source, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref calls));

        gate.SetResult();
        var values = await Task.WhenAll(requests);

        Assert.All(values, value => Assert.Equal(42, value));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task Stale_stampede_returns_stale_and_starts_exactly_one_refresh()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var calls = 0;

        Assert.Equal(1, await cache.GetOrCreateAsync(
            "key",
            _ => new ValueTask<AutoCacheFactoryResult<int>>(
                AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls))), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(2));
        var refreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Refresh(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            refreshEntered.TrySetResult();
            await releaseRefresh.Task;
            return AutoCacheFactoryResult<int>.FromValue(2);
        }

        var requests = Enumerable.Range(0, 2_000)
            .Select(_ => cache.GetOrCreateAsync("key", Refresh, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await refreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var staleValues = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.All(staleValues, value => Assert.Equal(1, value));
        Assert.Equal(2, Volatile.Read(ref calls)); // one cold load + one refresh

        releaseRefresh.SetResult();
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(2, await cache.GetOrCreateAsync("key", Refresh, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Fast_refresh_completion_does_not_reopen_stampede_race()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var sourceCalls = 0;

        Assert.Equal(1, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(2));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask<AutoCacheFactoryResult<int>> FastRefresh(CancellationToken _) =>
            ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref sourceCalls) + 1));

        var requests = Enumerable.Range(0, 2_000)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return await cache.GetOrCreateAsync("key", FastRefresh, cancellationToken: TestContext.Current.CancellationToken);
            }))
            .ToArray();

        start.SetResult();
        await Task.WhenAll(requests);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref sourceCalls));
        Assert.Equal(2, await cache.GetOrCreateAsync("key", FastRefresh, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_background_refresh_preserves_stale_value()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);

        Assert.Equal(7, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(7)), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(7, await cache.GetOrCreateAsync<int>(
            "key",
            _ => throw new InvalidOperationException("source down"), cancellationToken: TestContext.Current.CancellationToken));

        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(7, await cache.GetOrCreateAsync<int>(
            "key",
            _ => throw new InvalidOperationException("still down"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoValue_background_refresh_preserves_stale_value()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);

        Assert.Equal(9, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(9)), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(9, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.NoValue()), cancellationToken: TestContext.Current.CancellationToken));

        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(9, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.NoValue()), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fast_failed_refresh_is_backed_off_instead_of_retried_per_request()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var refreshCalls = 0;

        Assert.Equal(5, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(5)), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(2));

        ValueTask<AutoCacheFactoryResult<int>> FailingRefresh(CancellationToken _)
        {
            Interlocked.Increment(ref refreshCalls);
            throw new InvalidOperationException("source down");
        }

        Assert.Equal(5, await cache.GetOrCreateAsync("key", FailingRefresh, cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref refreshCalls));
        Assert.Equal(1, cache.RefreshFailureBackoffCount);

        var requests = Enumerable.Range(0, 2_000)
            .Select(_ => cache.GetOrCreateAsync("key", FailingRefresh, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        var values = await Task.WhenAll(requests);
        Assert.All(values, value => Assert.Equal(5, value));
        Assert.Equal(1, Volatile.Read(ref refreshCalls));

        time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(5, await cache.GetOrCreateAsync("key", FailingRefresh, cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref refreshCalls));
    }

    [Fact]
    public async Task Refresh_failure_backoff_cardinality_cap_is_configurable_and_strict()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(maxRefreshFailureBackoffEntries: 3), time);

        for (var i = 0; i < 5; i++)
        {
            await cache.GetOrCreateAsync(
                $"key-{i}",
                _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(i)), cancellationToken: TestContext.Current.CancellationToken);
        }

        time.Advance(TimeSpan.FromMinutes(2));

        ValueTask<AutoCacheFactoryResult<int>> Failure(CancellationToken _) =>
            throw new InvalidOperationException("source down");

        var staleReads = Enumerable.Range(0, 5)
            .Select(i => cache.GetOrCreateAsync($"key-{i}", Failure, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(staleReads);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(3, cache.RefreshFailureBackoffCount);
    }

    [Fact]
    public async Task Expired_value_is_not_served_and_cold_path_waits_for_source()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);

        Assert.Equal(1, await cache.GetOrCreateAsync(
            "key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(11));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            await gate.Task;
            return AutoCacheFactoryResult<int>.FromValue(2);
        }

        var request = cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.False(request.IsCompleted);

        gate.SetResult();
        Assert.Equal(2, await request);
    }

    [Fact]
    public async Task Expired_stampede_does_not_delete_the_winning_fresh_write()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);

        await cache.GetOrCreateAsync(
            "expired-race",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromMinutes(11));
        var sourceCalls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken token)
        {
            Interlocked.Increment(ref sourceCalls);
            await release.Task.WaitAsync(token);
            return AutoCacheFactoryResult<int>.FromValue(2);
        }

        var requests = Enumerable.Range(0, 2_000)
            .Select(_ => cache.GetOrCreateAsync("expired-race", Source, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await Task.Delay(20, TestContext.Current.CancellationToken);
        release.SetResult();
        var values = await Task.WhenAll(requests);

        Assert.Equal(1, sourceCalls);
        Assert.All(values, value => Assert.Equal(2, value));

        var cachedAgain = await cache.GetOrCreateAsync(
            "expired-race",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(99)), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, cachedAgain);
        Assert.Equal(1, sourceCalls);
    }

    [Fact]
    public async Task Missing_source_value_without_cache_throws_clear_exception()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);

        var error = await Assert.ThrowsAsync<AutoCacheSourceUnavailableException>(() =>
            cache.GetOrCreateAsync(
                "missing",
                _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.NoValue()), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("missing", error.Key);
    }

    [Fact]
    public async Task Factory_timeout_cancels_cooperative_source()
    {
        var time = TimeProvider.System;
        var cache = new TestCacheAdapter(
            Options(timeout: TimeSpan.FromMilliseconds(75)),
            time);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AutoCacheFactoryResult<int>.FromValue(1);
        }

        await Assert.ThrowsAsync<TimeoutException>(() => cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task Factory_timeout_is_enforced_even_when_source_ignores_cancellation()
    {
        var cache = new TestCacheAdapter(
            Options(timeout: TimeSpan.FromMilliseconds(60)),
            TimeProvider.System);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            await release.Task; // deliberately ignores the timeout token
            throw new InvalidOperationException("late fault");
        }

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() => cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));

        // Let the detached source finish faulting; AutoCache observes that late exception internally.
        release.SetResult();
        await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task Caller_cancellation_stops_waiting_but_does_not_cancel_shared_factory()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        var sourceCancellationObserved = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken sourceToken)
        {
            sourceCancellationObserved = sourceToken.IsCancellationRequested;
            await gate.Task;
            sourceCancellationObserved |= sourceToken.IsCancellationRequested;
            return AutoCacheFactoryResult<int>.FromValue(11);
        }

        using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var cancelledWaiter = cache.GetOrCreateAsync("key", Source, cancellationToken: callerCts.Token);
        var survivingWaiter = cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        Assert.False(sourceCancellationObserved);

        gate.SetResult();
        Assert.Equal(11, await survivingWaiter);
    }

    [Fact]
    public async Task Different_keys_execute_independently()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        var calls = 0;

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            return AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls));
        }

        var tasks = Enumerable.Range(0, 200)
            .Select(i => cache.GetOrCreateAsync($"key-{i}", Source, cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        Assert.Equal(200, Volatile.Read(ref calls));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task Per_entry_policy_overrides_defaults()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(freshFor: TimeSpan.FromHours(1), expireAfter: TimeSpan.FromHours(2)), time);
        var calls = 0;

        ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _) =>
            ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls)));

        var policy = new AutoCachePolicy
        {
            FreshFor = TimeSpan.FromSeconds(10),
            ExpireAfter = TimeSpan.FromMinutes(5),
            RefreshJitterRatio = 0
        };

        Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, policy, cancellationToken: TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, policy, cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, await cache.GetOrCreateAsync("key", Source, policy, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Completed_flights_are_removed_for_high_cardinality_keys()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);

        var tasks = Enumerable.Range(0, 10_000)
            .Select(i => cache.GetOrCreateAsync(
                $"unique-{i}",
                _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(i)), cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task Legacy_v3_overload_remains_usable()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        var calls = 0;

        var value = await cache.GetOrCreateAsync(
            "legacy",
            () => Task.FromResult((Interlocked.Increment(ref calls), true)),
            refreshAt: TimeSpan.FromMinutes(1),
            expireAt: TimeSpan.FromMinutes(5),
            timeout: TimeSpan.FromSeconds(1));

        Assert.Equal(1, value);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Expiration_is_rechecked_after_each_storage_read(int delayedRead)
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time)
        {
            ForceMissOnRead = delayedRead == 2 ? 1 : 0,
            AfterRead = async read =>
            {
                if (read != delayedRead) return;
                await Task.Yield();
                time.Advance(TimeSpan.FromMinutes(11));
            }
        };
        await cache.SetAsync("key", new CacheValue<int>
        {
            Value = 1, CreatedAt = Epoch,
            RefreshAt = Epoch.AddMinutes(1), ExpiresAt = Epoch.AddMinutes(10)
        }, TimeSpan.FromMinutes(10));
        var calls = 0;
        var result = await cache.GetOrCreateAsync("key", _ =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2));
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(59, 1, 1)]
    [InlineData(60, 1, 2)]
    [InlineData(599, 1, 2)]
    [InlineData(600, 2, 2)]
    public async Task Fresh_stale_and_hard_expiry_boundaries_are_exact(int seconds, int expected, int expectedCalls)
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time) { RetainExpiredValues = true };
        var calls = 0;
        ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _) =>
            ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls)));
        await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task Stale_requests_do_not_wait_for_synchronous_refresh_work()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                throw new TimeoutException("test did not release the factory");
            return ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2));
        }
        try
        {
            var first = Task.Run(() => cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            var readers = Enumerable.Range(0, 100).Select(_ => Task.Run(
                () => cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToArray();
            var values = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.All(values, value => Assert.Equal(1, value));
            Assert.Equal(1, calls);
        }
        finally
        {
            release.Set();
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synchronous_factory_times_out_and_late_completion_cannot_overwrite_retry(bool lateFault)
    {
        var cache = new TestCacheAdapter(Options(timeout: TimeSpan.FromMilliseconds(200)), TimeProvider.System);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            entered.TrySetResult();
            try
            {
                if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                    throw new TimeoutException("test did not release the factory");
                if (lateFault) throw new InvalidOperationException("late source failure");
                return ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(99));
            }
            finally { finished.TrySetResult(); }
        }
        try
        {
            var request = cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<TimeoutException>(() => request.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.False(finished.Task.IsCompleted);
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, cache.InFlightCount);
            Assert.Equal(2, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)), cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(3)), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timed_out_background_refresh_keeps_stale_and_backs_off()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(timeout: TimeSpan.FromMilliseconds(100)), time);
        await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return AutoCacheFactoryResult<int>.FromValue(99);
        }
        try
        {
            Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(1, cache.RefreshFailureBackoffCount);
            Assert.Equal(1, await cache.GetOrCreateAsync("key", Source, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, calls);
            time.Advance(TimeSpan.FromMinutes(9));
            Assert.Equal(2, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)), cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(0, cache.RefreshFailureBackoffCount);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Cancelling_all_waiters_does_not_prevent_successful_cache_fill()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken token)
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return AutoCacheFactoryResult<int>.FromValue(7);
        }
        var request = cache.GetOrCreateAsync("key", Source, cancellationToken: cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        release.SetResult();
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(7, await cache.GetOrCreateAsync<int>("key", _ => throw new InvalidOperationException("must be cached"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Precancelled_request_does_not_touch_storage_or_source()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System)
        {
            BeforeRead = _ => throw new InvalidOperationException("must not read")
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync<int>(
            "key", _ => throw new InvalidOperationException("must not invoke"), cancellationToken: cts.Token));
        Assert.Equal(0, cache.InFlightCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Backend_read_failure_is_reported_and_a_retry_can_succeed(int failingRead)
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System)
        {
            BeforeRead = read => read == failingRead ? Task.FromException(new IOException("backend down")) : Task.CompletedTask
        };
        await Assert.ThrowsAsync<IOException>(() => cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken));
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
        cache.BeforeRead = null;
        Assert.Equal(2, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_write_failure_cleans_up_and_preserves_existing_stale(bool background)
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        if (background)
        {
            await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromMinutes(2));
        }
        cache.BeforeWrite = () => Task.FromException(new IOException("write failed"));
        var request = cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)), cancellationToken: TestContext.Current.CancellationToken);
        if (background) Assert.Equal(1, await request);
        else await Assert.ThrowsAsync<IOException>(() => request);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
        var (entry, found) = await cache.GetAsync<CacheValue<int>>("key");
        Assert.Equal(background, found);
        if (found) Assert.Equal(1, entry.Value);
        cache.BeforeWrite = null;
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(3, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(3)), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Expired_backoff_slots_are_reclaimed_and_success_clears_tracking()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(maxRefreshFailureBackoffEntries: 2), time);
        for (var i = 0; i < 3; i++)
            await cache.GetOrCreateAsync($"key-{i}", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        ValueTask<AutoCacheFactoryResult<int>> Failure(CancellationToken _) => ValueTask.FromResult(AutoCacheFactoryResult<int>.NoValue());
        for (var i = 0; i < 2; i++) await cache.GetOrCreateAsync($"key-{i}", Failure, cancellationToken: TestContext.Current.CancellationToken);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, cache.RefreshFailureBackoffCount);
        time.Advance(TimeSpan.FromSeconds(5));
        await cache.GetOrCreateAsync("key-2", Failure, cancellationToken: TestContext.Current.CancellationToken);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, cache.RefreshFailureBackoffCount);
        time.Advance(TimeSpan.FromSeconds(5));
        await cache.GetOrCreateAsync("key-2", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)), cancellationToken: TestContext.Current.CancellationToken);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.RefreshFailureBackoffCount);
    }

    [Fact]
    public async Task Json_envelope_roundtrip_preserves_value_and_expiration()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time) { SerializeValues = true };
        var expected = new Product(42, "محصول");
        Assert.Equal(expected, await cache.GetOrCreateAsync("product", _ => ValueTask.FromResult(AutoCacheFactoryResult<Product>.FromValue(expected)), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(expected, await cache.GetOrCreateAsync<Product>("product", _ => throw new InvalidOperationException("must use serialized cache"), cancellationToken: TestContext.Current.CancellationToken));
        var (entry, found) = await cache.GetAsync<CacheValue<Product>>("product");
        Assert.True(found);
        Assert.Equal(Epoch.AddMinutes(1), entry.RefreshAt);
        Assert.Equal(Epoch.AddMinutes(10), entry.ExpiresAt);
        time.Advance(TimeSpan.FromMinutes(10));
        await Assert.ThrowsAsync<AutoCacheSourceUnavailableException>(() => cache.GetOrCreateAsync("product", _ => ValueTask.FromResult(AutoCacheFactoryResult<Product>.NoValue()), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Explicit_null_and_default_values_are_cacheable()
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        Assert.Null(await cache.GetOrCreateAsync("null", _ => ValueTask.FromResult(AutoCacheFactoryResult<string?>.FromValue(null)), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(await cache.GetOrCreateAsync<string?>("null", _ => throw new InvalidOperationException("must cache null"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, await cache.GetOrCreateAsync("zero", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(0)), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, await cache.GetOrCreateAsync<int>("zero", _ => throw new InvalidOperationException("must cache zero"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(0.51)]
    public async Task Invalid_jitter_is_rejected_at_construction_and_per_call(double jitter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestCacheAdapter(new AutoCacheOptions { RefreshJitterRatio = jitter }, TimeProvider.System));
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.GetOrCreateAsync("key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), new AutoCachePolicy { RefreshJitterRatio = jitter }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Jitter_moves_refresh_earlier_without_changing_hard_expiration()
    {
        var time = new ManualTimeProvider(Epoch);
        var cache = new TestCacheAdapter(Options(), time);
        await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)),
            new AutoCachePolicy { RefreshJitterRatio = 0.5 }, cancellationToken: TestContext.Current.CancellationToken);
        var (entry, _) = await cache.GetAsync<CacheValue<int>>("key");
        Assert.InRange(entry.RefreshAt, Epoch.AddSeconds(30), Epoch.AddMinutes(1));
        Assert.Equal(Epoch.AddMinutes(10), entry.ExpiresAt);
    }

    [Fact]
    public async Task Infinite_factory_timeout_supplies_a_non_cancelable_token()
    {
        var cache = new TestCacheAdapter(Options(timeout: Timeout.InfiniteTimeSpan), TimeProvider.System);
        Assert.Equal(1, await cache.GetOrCreateAsync("key", async token =>
        {
            Assert.False(token.CanBeCanceled);
            await Task.Yield();
            return AutoCacheFactoryResult<int>.FromValue(1);
        }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cold_failure_is_shared_by_waiters_and_does_not_poison_retry(bool noValue)
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async ValueTask<AutoCacheFactoryResult<int>> Failure(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(token);
            if (noValue) return AutoCacheFactoryResult<int>.NoValue();
            throw new IOException("source unavailable");
        }
        var requests = Enumerable.Range(0, 200).Select(_ => cache.GetOrCreateAsync("key", Failure,
            cancellationToken: TestContext.Current.CancellationToken)).ToArray();
        release.SetResult();
        foreach (var request in requests)
        {
            if (noValue) await Assert.ThrowsAsync<AutoCacheSourceUnavailableException>(() => request);
            else await Assert.ThrowsAsync<IOException>(() => request);
        }
        Assert.Equal(1, calls);
        await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.InFlightCount);
        Assert.Equal(2, await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(2)),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Caller_can_cancel_a_delayed_initial_storage_read()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new TestCacheAdapter(Options(), TimeProvider.System)
        {
            AfterRead = async _ => { entered.SetResult(); await release.Task; }
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = 0;
        var request = cache.GetOrCreateAsync("key", _ =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1));
        }, cancellationToken: cts.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.Equal(0, calls);
            Assert.Equal(0, cache.InFlightCount);
        }
        finally { release.SetResult(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Invalid_key_is_rejected_before_storage(string key)
    {
        var cache = new TestCacheAdapter(Options(), TimeProvider.System)
        {
            BeforeRead = _ => throw new InvalidOperationException("must not read")
        };
        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrCreateAsync(key,
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)), cancellationToken: TestContext.Current.CancellationToken));
    }

    public sealed record Product(int Id, string Name);
}
