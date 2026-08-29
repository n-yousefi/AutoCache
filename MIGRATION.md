# Migrating from AutoCache 3.x to 4.x

## Preserved

- `ICacheAdapter.SetAsync<T>(string, T, TimeSpan)`
- `ICacheAdapter.GetAsync<T>(string)`
- `ICacheAdapter.RemoveAsync(string)`
- The 3.x `GetOrCreateAsync` calling shape with `(value, hasValue)`
- Constructor accepting default refresh, expiration, and timeout durations

## Behavioral fixes

### Stale refresh is now coalesced

3.x could start multiple background refreshes when many callers simultaneously observed a stale item. 4.x uses the same single-flight table for stale refreshes and cold misses.

### Failed refresh does not delete stale data

A failed or empty background refresh now leaves the stale entry untouched until its hard expiration.

### Timeout applies to the source factory

The modern overload receives a `CancellationToken` that is cancelled at `FactoryTimeout`. Source code should pass that token to HTTP/database calls.

### Caller cancellation does not cancel shared work

A caller can stop waiting using its request token. The underlying single-flight continues because other callers may depend on it.

## Recommended migration

Keep your existing adapter first and update call sites gradually to the token-aware overload:

```csharp
await cache.GetOrCreateAsync(
    key,
    async token => AutoCacheFactoryResult<MyType>.FromValue(
        await repository.LoadAsync(token)),
    cancellationToken: requestAborted);
```

## Cache data during deployment

AutoCache 4 persists a richer cache envelope with UTC `DateTimeOffset` timestamps and an explicit hard-expiration timestamp. For distributed stores, use a new cache-key prefix/version or clear AutoCache 3.x entries during rollout instead of assuming old serialized envelopes are wire-compatible.

## `NoValue` on a cold miss

The 3.x calling shape is preserved, but 4.x intentionally makes a cold `HasValue == false` explicit by throwing `AutoCacheSourceUnavailableException`. Background `NoValue` results still return and preserve the stale value. This is a major-version behavioral change rather than silently returning a default value.
