namespace AutoCache;

/// <summary>
/// Thrown when no usable cache value exists and the source factory fails to produce one.
/// </summary>
public sealed class AutoCacheSourceUnavailableException : Exception
{
    public AutoCacheSourceUnavailableException(string key)
        : base($"The source did not produce a value for cache key '{key}', and no usable cached value exists.")
    {
        Key = key;
    }

    public string Key { get; }
}
