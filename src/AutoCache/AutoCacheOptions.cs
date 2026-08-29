namespace AutoCache;

/// <summary>
/// Default stale-while-revalidate policy used by <see cref="CacheAdapter"/>.
/// </summary>
public sealed class AutoCacheOptions
{
    /// <summary>How long a value is considered fresh.</summary>
    public TimeSpan FreshFor { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Total lifetime of a cached value, including its stale window.</summary>
    public TimeSpan ExpireAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Maximum source-factory execution time.</summary>
    public TimeSpan FactoryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum delay before retrying a failed background refresh for the same key.
    /// This prevents a fast-failing source from being hammered by stale traffic.
    /// </summary>
    public TimeSpan RefreshFailureBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Randomly moves the refresh boundary earlier to reduce synchronized refreshes across keys/workloads.
    /// Valid range is 0 through 0.5. A value of 0 disables jitter.
    /// </summary>
    public double RefreshJitterRatio { get; init; } = 0.05;

    /// <summary>
    /// Maximum number of distinct failed-refresh backoff entries retained by one adapter instance.
    /// This is a hard process-local memory bound for outage tracking.
    /// </summary>
    public int MaxRefreshFailureBackoffEntries { get; init; } = 10_000;

    internal void Validate()
    {
        ValidateDurations(FreshFor, ExpireAfter, FactoryTimeout, RefreshFailureBackoff, RefreshJitterRatio);

        if (MaxRefreshFailureBackoffEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRefreshFailureBackoffEntries), "MaxRefreshFailureBackoffEntries must be greater than zero.");
    }

    internal static void ValidateDurations(
        TimeSpan freshFor,
        TimeSpan expireAfter,
        TimeSpan factoryTimeout,
        TimeSpan refreshFailureBackoff,
        double refreshJitterRatio)
    {
        if (freshFor <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(freshFor), "FreshFor must be greater than zero.");

        if (expireAfter <= freshFor)
            throw new ArgumentOutOfRangeException(nameof(expireAfter), "ExpireAfter must be greater than FreshFor.");

        if (factoryTimeout <= TimeSpan.Zero && factoryTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(factoryTimeout), "FactoryTimeout must be positive or Timeout.InfiniteTimeSpan.");

        if (refreshFailureBackoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refreshFailureBackoff), "RefreshFailureBackoff cannot be negative.");

        if (!double.IsFinite(refreshJitterRatio) || refreshJitterRatio is < 0 or > 0.5)
            throw new ArgumentOutOfRangeException(nameof(refreshJitterRatio), "RefreshJitterRatio must be between 0 and 0.5.");
    }
}

/// <summary>Per-entry overrides. Null properties inherit adapter defaults.</summary>
public sealed class AutoCachePolicy
{
    public TimeSpan? FreshFor { get; init; }
    public TimeSpan? ExpireAfter { get; init; }
    public TimeSpan? FactoryTimeout { get; init; }
    public TimeSpan? RefreshFailureBackoff { get; init; }
    public double? RefreshJitterRatio { get; init; }
}
