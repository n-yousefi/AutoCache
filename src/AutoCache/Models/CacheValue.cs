namespace AutoCache.Models;

/// <summary>
/// The envelope persisted by AutoCache. Public setters intentionally support serializers used by Redis adapters.
/// </summary>
public sealed class CacheValue<T>
{
    public T Value { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset RefreshAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsFresh(DateTimeOffset now) => now < RefreshAt && now < ExpiresAt;

    public bool IsStale(DateTimeOffset now) => now >= RefreshAt && now < ExpiresAt;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
