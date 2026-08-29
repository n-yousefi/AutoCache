namespace AutoCache.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public ManualTimeProvider(DateTimeOffset initial) => _ticks = initial.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan amount) => Interlocked.Add(ref _ticks, amount.Ticks);
}
