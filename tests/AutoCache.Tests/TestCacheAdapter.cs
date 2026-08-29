using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoCache.Tests;

internal sealed class TestCacheAdapter : CacheAdapter
{
    private sealed record StoredValue(object Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, StoredValue> _store = new();
    private readonly TimeProvider _timeProvider;
    private int _readCount;

    public Func<int, Task>? BeforeRead { get; set; }
    public Func<int, Task>? AfterRead { get; set; }
    public Func<Task>? BeforeWrite { get; set; }
    public int ForceMissOnRead { get; set; }
    public bool RetainExpiredValues { get; init; }
    public bool SerializeValues { get; init; }

    public TestCacheAdapter(AutoCacheOptions options, TimeProvider timeProvider)
        : base(options, timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public override async Task SetAsync<T>(string key, T value, TimeSpan expireAt)
    {
        if (BeforeWrite is not null)
            await BeforeWrite();
        _store[key] = new StoredValue(SerializeValues ? JsonSerializer.Serialize(value) : value!, _timeProvider.GetUtcNow() + expireAt);
    }

    public override async Task<(T Value, bool Found)> GetAsync<T>(string key)
    {
        var read = Interlocked.Increment(ref _readCount);
        if (BeforeRead is not null)
            await BeforeRead(read);
        var found = _store.TryGetValue(key, out var stored) && read != ForceMissOnRead &&
            (RetainExpiredValues || _timeProvider.GetUtcNow() < stored.ExpiresAt);
        var value = found ? (SerializeValues ? JsonSerializer.Deserialize<T>((string)stored!.Value)! : (T)stored!.Value) : default!;
        // Snapshot before the delay, as a remote backend can do before delivering its response.
        if (AfterRead is not null)
            await AfterRead(read);
        return (value, found);
    }

    public override Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
