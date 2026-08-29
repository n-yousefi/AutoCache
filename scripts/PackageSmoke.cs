using System.Collections.Concurrent;
using System.Text.Json;
using AutoCache;
using AutoCache.Models;

var clock = new SmokeClock();
ICacheAdapter cache = new JsonAdapter(clock);
var calls = 0;
async ValueTask<AutoCacheFactoryResult<string>> Source(CancellationToken token)
{
    Interlocked.Increment(ref calls);
    await Task.Delay(5, token);
    return AutoCacheFactoryResult<string>.FromValue("محصول");
}
var values = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => cache.GetOrCreateAsync("key", Source)));
if (values.Any(value => value != "محصول") || calls != 1) throw new Exception("Package single-flight/serialization failed");

clock.Advance(TimeSpan.FromMinutes(2));
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
async ValueTask<AutoCacheFactoryResult<string>> Refresh(CancellationToken token)
{
    await release.Task.WaitAsync(token);
    return AutoCacheFactoryResult<string>.FromValue("updated");
}
if (await cache.GetOrCreateAsync("key", Refresh).WaitAsync(TimeSpan.FromSeconds(5)) != "محصول")
    throw new Exception("Package stale read failed");
release.SetResult();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
while (true)
{
    var (entry, found) = await cache.GetAsync<CacheValue<string>>("key");
    if (found && entry.Value == "updated") break;
    await Task.Delay(10, deadline.Token);
}
var legacy = await cache.GetOrCreateAsync("legacy", () => Task.FromResult((42, true)));
if (legacy != 42) throw new Exception("Package legacy overload failed");
Console.WriteLine($"PASS: nupkg consumer on {Environment.Version}: serialized values, single-flight, stale refresh, legacy API");

sealed class SmokeClock : TimeProvider
{
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
}

sealed class JsonAdapter(TimeProvider clock) : CacheAdapter(new AutoCacheOptions { RefreshJitterRatio = 0, FreshFor = TimeSpan.FromMinutes(1) }, clock)
{
    private readonly ConcurrentDictionary<string, string> _store = new();
    public override async Task<(T Value, bool Found)> GetAsync<T>(string key)
    {
        await Task.Yield();
        return _store.TryGetValue(key, out var json) ? (JsonSerializer.Deserialize<T>(json)!, true) : (default!, false);
    }
    public override Task SetAsync<T>(string key, T value, TimeSpan expireAt)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }
    public override Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
