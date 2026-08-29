using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoCache.LoadTests;

internal sealed class LoadCacheAdapter : CacheAdapter
{
    private sealed record StoredValue(object Value, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, StoredValue> _store = new();
    private readonly TimeProvider _timeProvider;
    public int StorageDelayMs { get; init; }
    public bool SerializeValues { get; init; }

    public LoadCacheAdapter(AutoCacheOptions options, TimeProvider timeProvider)
        : base(options, timeProvider) => _timeProvider = timeProvider;

    public override async Task SetAsync<T>(string key, T value, TimeSpan expireAt)
    {
        if (StorageDelayMs > 0) await Task.Delay(StorageDelayMs);
        _store[key] = new StoredValue(SerializeValues ? JsonSerializer.Serialize(value) : value!, _timeProvider.GetUtcNow() + expireAt);
    }

    public override async Task<(T Value, bool Found)> GetAsync<T>(string key)
    {
        var found = _store.TryGetValue(key, out var stored) && _timeProvider.GetUtcNow() < stored.ExpiresAt;
        var value = found ? (SerializeValues ? JsonSerializer.Deserialize<T>((string)stored!.Value)! : (T)stored!.Value) : default!;
        if (StorageDelayMs > 0) await Task.Delay(StorageDelayMs);
        return (value, found);
    }

    public override Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
