namespace AutoCache.Internal;

internal readonly record struct ResolvedPolicy(
    TimeSpan FreshFor,
    TimeSpan ExpireAfter,
    TimeSpan FactoryTimeout,
    TimeSpan RefreshFailureBackoff,
    double RefreshJitterRatio);
