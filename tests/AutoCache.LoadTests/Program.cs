using System.Collections.Concurrent;
using System.Diagnostics;

namespace AutoCache.LoadTests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var requests = ReadInt(args, "--requests", 250_000);
        var concurrency = ReadInt(args, "--concurrency", 1_024);
        var keyCount = ReadInt(args, "--keys", 1_000);
        var storageDelayMs = ReadInt(args, "--storage-delay-ms", 0);
        var serialize = args.Contains("--serialize");
        var rounds = ReadInt(args, "--rounds", 5);

        Console.WriteLine($"AutoCache load tests: runtime={Environment.Version}, requests={requests:N0}, concurrency={concurrency:N0}, keys={keyCount:N0}, storageDelayMs={storageDelayMs}, serialize={serialize}, rounds={rounds}");

        try
        {
            await ColdStampede(concurrency);
            await StaleRefreshStorm(concurrency);
            await ExpiredStampede(concurrency);
            await FastFailureStorm(concurrency);
            await FailureBackoffCardinality();
            await MixedHighCardinality(requests, concurrency, keyCount, storageDelayMs, serialize);
            await LifecycleCycles(rounds);
            Console.WriteLine("PASS: all load-test invariants held.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
    }

    private static async Task ColdStampede(int concurrency)
    {
        var cache = CreateCache(TimeProvider.System);
        var calls = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            await start.Task.WaitAsync(token);
            await Task.Delay(20, token);
            return AutoCacheFactoryResult<int>.FromValue(123);
        }

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => cache.GetOrCreateAsync("hot-key", Source))
            .ToArray();

        await Task.Delay(25);
        start.SetResult();
        var values = await Task.WhenAll(tasks);
        sw.Stop();

        Require(calls == 1, $"cold stampede invoked source {calls} times; expected 1");
        Require(values.All(static x => x == 123), "cold stampede returned inconsistent values");
        await cache.WaitForIdleAsync();
        Require(cache.InFlightCount == 0, "cold stampede left in-flight entries behind");

        Console.WriteLine($"[cold]  {concurrency:N0} simultaneous requests -> 1 source call in {sw.Elapsed.TotalMilliseconds:N1} ms");
    }

    private static async Task StaleRefreshStorm(int concurrency)
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(time);
        var calls = 0;

        await cache.GetOrCreateAsync(
            "hot-key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(Interlocked.Increment(ref calls))));

        time.Advance(TimeSpan.FromMinutes(2));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Refresh(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            refreshStarted.TrySetResult();
            await gate.Task.WaitAsync(token);
            return AutoCacheFactoryResult<int>.FromValue(2);
        }

        var latencies = new ConcurrentBag<double>();
        var sw = Stopwatch.StartNew();
        var tasks = RunConcurrent(concurrency, async _ =>
        {
            var started = Stopwatch.GetTimestamp();
            var value = await cache.GetOrCreateAsync("hot-key", Refresh);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return value;
        });

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var values = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        Require(values.All(static x => x == 1), "stale storm did not return the stale value immediately");
        Require(calls == 2, $"stale storm invoked refresh {calls - 1} times; expected exactly 1");

        gate.SetResult();
        await cache.WaitForIdleAsync();
        Require(cache.InFlightCount == 0, "stale refresh left in-flight entries behind");

        var ordered = latencies.OrderBy(static x => x).ToArray();
        Console.WriteLine($"[stale] {concurrency:N0} simultaneous requests -> 1 refresh; p50={Percentile(ordered, 0.50):N2} ms p95={Percentile(ordered, 0.95):N2} ms p99={Percentile(ordered, 0.99):N2} ms");
    }

    private static async Task ExpiredStampede(int concurrency)
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(time);
        var sourceCalls = 0;

        await cache.GetOrCreateAsync(
            "expired-key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(1)));

        time.Advance(TimeSpan.FromMinutes(11));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AutoCacheFactoryResult<int>> Source(CancellationToken token)
        {
            Interlocked.Increment(ref sourceCalls);
            await gate.Task.WaitAsync(token);
            return AutoCacheFactoryResult<int>.FromValue(2);
        }

        var requests = Enumerable.Range(0, concurrency)
            .Select(_ => cache.GetOrCreateAsync("expired-key", Source))
            .ToArray();

        await Task.Delay(25);
        Require(sourceCalls == 1, $"expired stampede invoked source {sourceCalls} times before release; expected 1");
        gate.SetResult();
        var values = await Task.WhenAll(requests);

        Require(values.All(static x => x == 2), "expired stampede returned an expired value");
        Require(sourceCalls == 1, $"expired stampede invoked source {sourceCalls} times; expected 1");
        await cache.WaitForIdleAsync();
        Require(cache.InFlightCount == 0, "expired stampede left in-flight entries behind");

        Console.WriteLine($"[expire] {concurrency:N0} simultaneous expired requests -> 1 blocking source call");
    }

    private static async Task FastFailureStorm(int concurrency)
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(time);
        var refreshCalls = 0;

        await cache.GetOrCreateAsync(
            "failure-key",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(10)));
        time.Advance(TimeSpan.FromMinutes(2));

        ValueTask<AutoCacheFactoryResult<int>> FailingRefresh(CancellationToken _)
        {
            Interlocked.Increment(ref refreshCalls);
            throw new InvalidOperationException("simulated dependency outage");
        }

        // Establish the backoff with one deliberately fast failure.
        Require(await cache.GetOrCreateAsync("failure-key", FailingRefresh) == 10, "failure storm lost stale value");
        await cache.WaitForIdleAsync();

        var tasks = RunConcurrent(concurrency * 10, _ => cache.GetOrCreateAsync("failure-key", FailingRefresh));
        var values = await Task.WhenAll(tasks);

        Require(values.All(static x => x == 10), "failure storm did not preserve stale value");
        Require(refreshCalls == 1, $"failure storm retried source {refreshCalls} times inside backoff; expected 1");
        Require(cache.RefreshFailureBackoffCount == 1, "failure backoff was not tracked");

        Console.WriteLine($"[fail]  {concurrency * 10:N0} stale requests during outage -> 1 failed refresh, then backoff");
    }

    private static async Task FailureBackoffCardinality()
    {
        const int keys = 12_000;
        const int maxTrackedBackoffs = 10_000;
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(time);

        var seed = Enumerable.Range(0, keys)
            .Select(i => cache.GetOrCreateAsync(
                $"outage-{i}",
                _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(i))))
            .ToArray();
        await Task.WhenAll(seed);

        time.Advance(TimeSpan.FromMinutes(2));

        ValueTask<AutoCacheFactoryResult<int>> Failure(CancellationToken _) =>
            throw new InvalidOperationException("simulated dependency outage");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleReads = Enumerable.Range(0, keys)
            .Select(i => Task.Run(async () =>
            {
                await start.Task;
                return await cache.GetOrCreateAsync($"outage-{i}", Failure);
            }))
            .ToArray();
        start.SetResult();
        var values = await Task.WhenAll(staleReads);
        await cache.WaitForIdleAsync();

        Require(values.Length == keys, "high-cardinality outage lost stale reads");
        Require(cache.RefreshFailureBackoffCount == maxTrackedBackoffs,
            $"failure backoff tracker contains {cache.RefreshFailureBackoffCount} entries; expected hard cap {maxTrackedBackoffs}");

        Console.WriteLine($"[bound] {keys:N0} independently failing stale keys -> backoff tracker capped at {maxTrackedBackoffs:N0}");
    }

    private static async Task MixedHighCardinality(int requests, int concurrency, int keyCount, int storageDelayMs, bool serialize)
    {
        // Freeze envelope time so this scenario tests one fresh population regardless of
        // machine speed. LifecycleCycles separately advances time through expiry/outages.
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(time, storageDelayMs, serialize);
        var callsByKey = new int[keyCount];
        var visited = new int[keyCount];
        var completed = 0;
        var next = 0;
        var latencies = new double[requests];
        var sw = Stopwatch.StartNew();

        var workers = RunConcurrent(concurrency, async _ =>
        {
            while (true)
            {
                var index = Interlocked.Increment(ref next) - 1;
                if (index >= requests)
                    return true;

                // First visit every requested key. Then distribute 80% of the remaining
                // traffic over hot keys; advance the cold index independently of the decision.
                var tail = index - keyCount;
                var keyIndex = index < keyCount ? index : tail % 10 < 8
                    ? ((tail / 10) * 8 + tail % 10) % Math.Max(1, keyCount / 5)
                    : ((tail / 10) * 2 + tail % 10 - 8) % keyCount;
                Interlocked.Exchange(ref visited[keyIndex], 1);

                var started = Stopwatch.GetTimestamp();
                var value = await cache.GetOrCreateAsync($"key-{keyIndex}", async token =>
                {
                    Interlocked.Increment(ref callsByKey[keyIndex]);
                    await Task.Delay(1, token);
                    return AutoCacheFactoryResult<int>.FromValue(keyIndex);
                });
                latencies[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Require(value == keyIndex, "mixed load returned a value for another key");
                Interlocked.Increment(ref completed);
            }
        });

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromMinutes(3));
        sw.Stop();
        await cache.WaitForIdleAsync();

        Require(completed == requests, $"completed {completed} of {requests} requests");
        Require(cache.InFlightCount == 0, $"{cache.InFlightCount} in-flight entries remained after mixed load");
        var actualKeys = visited.Sum();
        Require(actualKeys == Math.Min(requests, keyCount), "mixed load did not cover the expected keys");
        for (var i = 0; i < keyCount; i++)
            Require(callsByKey[i] == visited[i], $"key-{i}: {callsByKey[i]} source calls; expected {visited[i]}");

        Array.Sort(latencies);
        var rps = requests / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"[mixed] {requests:N0} requests, {actualKeys:N0} actual keys -> {callsByKey.Sum():N0} source calls, {rps:N0} req/s; p95={Percentile(latencies, 0.95):N3} ms p99={Percentile(latencies, 0.99):N3} ms; elapsed={sw.Elapsed.TotalSeconds:N2} s");
    }

    private static async Task LifecycleCycles(int rounds)
    {
        const int keys = 64;
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(time, serialize: true);
        var generation = 0;
        await Task.WhenAll(RunConcurrent(keys, i => cache.GetOrCreateAsync($"cycle-{i}",
            _ => ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(generation)))));
        for (var round = 0; round < rounds; round++)
        {
            time.Advance(TimeSpan.FromMinutes(2));
            var failureCalls = 0;
            ValueTask<AutoCacheFactoryResult<int>> Failure(CancellationToken _)
            {
                Interlocked.Increment(ref failureCalls);
                return ValueTask.FromException<AutoCacheFactoryResult<int>>(new IOException("outage"));
            }
            var stale = await Task.WhenAll(RunConcurrent(keys * 8, i => cache.GetOrCreateAsync($"cycle-{i % keys}", Failure)));
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Require(stale.All(value => value == generation), "lifecycle outage lost stale values");
            Require(failureCalls == keys && cache.RefreshFailureBackoffCount == keys, "lifecycle backoff did not coalesce failures");
            await Task.WhenAll(RunConcurrent(keys * 8, i => cache.GetOrCreateAsync($"cycle-{i % keys}", Failure)));
            Require(failureCalls == keys, "lifecycle retried inside backoff");
            time.Advance(TimeSpan.FromSeconds(5));
            generation++;
            var refreshCalls = 0;
            ValueTask<AutoCacheFactoryResult<int>> Success(CancellationToken _)
            {
                Interlocked.Increment(ref refreshCalls);
                return ValueTask.FromResult(AutoCacheFactoryResult<int>.FromValue(generation));
            }
            await Task.WhenAll(RunConcurrent(keys * 8, i => cache.GetOrCreateAsync($"cycle-{i % keys}", Success)));
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Require(refreshCalls == keys && cache.RefreshFailureBackoffCount == 0, "lifecycle recovery did not clear backoff");
            time.Advance(TimeSpan.FromMinutes(10));
            generation++;
            refreshCalls = 0;
            var expired = await Task.WhenAll(RunConcurrent(keys * 8, i => cache.GetOrCreateAsync($"cycle-{i % keys}", Success)));
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Require(expired.All(value => value == generation) && refreshCalls == keys, "lifecycle expiration did not coalesce replacement");
            Require(cache.InFlightCount == 0 && cache.RefreshFailureBackoffCount == 0, "lifecycle state leaked");
        }
        Console.WriteLine($"[cycle] {rounds:N0} outage/recovery/expiration cycles across {keys} serialized keys; no retained flights/backoffs");
    }

    private static Task<T>[] RunConcurrent<T>(int count, Func<int, Task<T>> operation)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            await start.Task;
            return await operation(i);
        })).ToArray();
        start.SetResult();
        return tasks;
    }

    private static LoadCacheAdapter CreateCache(TimeProvider timeProvider, int storageDelayMs = 0, bool serialize = false) => new(
        new AutoCacheOptions
        {
            FreshFor = TimeSpan.FromMinutes(1),
            ExpireAfter = TimeSpan.FromMinutes(10),
            FactoryTimeout = TimeSpan.FromSeconds(10),
            RefreshJitterRatio = 0
        },
        timeProvider) { StorageDelayMs = storageDelayMs, SerializeValues = serialize };

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
            return 0;

        var index = (int)Math.Clamp(Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
        return values[index];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
