namespace AutoCache;

/// <summary>Represents the result of a source fetch.</summary>
public readonly record struct AutoCacheFactoryResult<T>(T Value, bool HasValue)
{
    public static AutoCacheFactoryResult<T> FromValue(T value) => new(value, true);

    public static AutoCacheFactoryResult<T> NoValue() => new(default!, false);
}
