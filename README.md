# AutoCache

Cache misses often causes a large number of requests being referred to the database at the same time, until the data is cached again. Under pressure, this can reduce system performance and functionality.

# How it works?

With AutoCache, outdated cache keys will remain alive until they are expired.
Suppose 100,000 requests arived at same time, looking for an outdated cache item. All requests get outdated data from cache and cache update task will be triggered only once (Only one request referred to database to update the cache).

When the cache item data, the expire time (ttl) and outdate time of cache key, updated too.

# Installation

[![NuGet](https://img.shields.io/badge/AutoCache-nuget-green)](https://www.nuget.org/packages/AutoCache/)

First, [install NuGet](http://docs.nuget.org/docs/start-here/installing-nuget). Then, install [AutoCache](https://www.nuget.org/packages/AutoCache/) from the package manager console:

```
PM> Install-Package AutoCache
```

### How do I get started?

    public abstract class CacheAdapter
    {
        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, DateTime expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }

First create an adapter for your caching service (or database), by inheriting from the "BaseCache" abstract class and implement abstract methods.

    public class MyCacheAdapter : CacheAdapter{
        ...
    }

Then instantiate your cache adapter:

    ICacheAdapter cache = new MyCacheAdapter(
            serviceScopeFactory, //IServiceScopeFactory
            60000, //defaulOutdatedAtMiliSecond
            3600000); //defaultExpireAtMiliSecond

You can inject it in ConfigureServices:

    services.AddSingleton<ICacheAdapter, cache>(); // your cache adapter

Now you can use it:

    public interface IToDoService
    {
        Task<int> GetAsync();
    }

    public class ToDoService: IToDoService
    {
        public virtual async Task<int> GetAsync() {
            // read from DB
            throw new NotImplementedException();
        };
    }

    public class CachedTodoService:ToDoService
    {
        private readonly ICacheAdapter _cache;
        public CachedTodoService(ICacheAdapter cache) => _cache = cache;

        public override async Task<int> GetAsync() =>
            await _cache.GetOrCreateAsync<int, IToDoService>("todo_service_cache_key",
                async (toDoService, updateIsInProgress) =>
                {
                    try
                    {
                        var value = await toDoService.GetAsync();
                        return (value, true);
                    }
                    catch (Exception ex)
                    {
                        return (0, false);
                    }
                });
    }
