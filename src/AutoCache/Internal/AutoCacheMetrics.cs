using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AutoCache.Internal;

internal static class AutoCacheMetrics
{
    private static readonly Meter Meter = new(AutoCacheDiagnostics.MeterName, "4.0.0-alpha.1");
    private static readonly ActivitySource ActivitySource = new(AutoCacheDiagnostics.ActivitySourceName, "4.0.0-alpha.1");

    private static readonly Counter<long> FreshHits = Meter.CreateCounter<long>("autocache.fresh_hits");
    private static readonly Counter<long> StaleHits = Meter.CreateCounter<long>("autocache.stale_hits");
    private static readonly Counter<long> Misses = Meter.CreateCounter<long>("autocache.misses");
    private static readonly Counter<long> FactoryCalls = Meter.CreateCounter<long>("autocache.factory_calls");
    private static readonly Counter<long> FactoryFailures = Meter.CreateCounter<long>("autocache.factory_failures");
    private static readonly Counter<long> RefreshesSuppressed = Meter.CreateCounter<long>("autocache.refreshes_suppressed");
    private static readonly Histogram<double> FactoryDuration = Meter.CreateHistogram<double>("autocache.factory_duration", "ms");

    internal static void FreshHit() => FreshHits.Add(1);
    internal static void StaleHit() => StaleHits.Add(1);
    internal static void Miss() => Misses.Add(1);
    internal static void FactoryCall() => FactoryCalls.Add(1);
    internal static void FactoryFailure() => FactoryFailures.Add(1);
    internal static void RefreshSuppressed() => RefreshesSuppressed.Add(1);
    internal static void RecordFactoryDuration(TimeSpan duration) => FactoryDuration.Record(duration.TotalMilliseconds);

    internal static Activity? StartFactoryActivity(bool background)
    {
        var activity = ActivitySource.StartActivity("autocache.factory", ActivityKind.Internal);
        activity?.SetTag("autocache.background", background);
        return activity;
    }
}
