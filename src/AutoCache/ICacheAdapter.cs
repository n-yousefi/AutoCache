namespace AutoCache;

/// <summary>
/// Storage adapter plus stale-while-revalidate operations.
/// </summary>
public interface ICacheAdapter
{
    Task SetAsync<T>(string key, T value, TimeSpan expireAt);

    Task<(T Value, bool Found)> GetAsync<T>(string key);

    Task RemoveAsync(string key);

    /// <summary>
    /// Compatibility API for AutoCache 3.x callers.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<(T Value, bool HasValue)>> resourceFetch,
        TimeSpan? refreshAt = null,
        TimeSpan? expireAt = null,
        TimeSpan? timeout = null);

    /// <summary>
    /// Modern API with source cancellation, per-entry policy overrides, and true factory timeout.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<AutoCacheFactoryResult<T>>> resourceFetch,
        AutoCachePolicy? policy = null,
        CancellationToken cancellationToken = default);
}
