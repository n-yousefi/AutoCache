# Why AutoCache?

Cache misses often causes a large number of requests being referred to the database at the same time, until the data is cached again. This can reduce system performance and functionality.

# How it works?

With AutoCache, outdated cache keys will remain alive until they are expired.
Suppose hundreds of requests arived at same time, looking for an outdated cache item. Instead of referring them to the database, all requests will get outdated data from cache and the update task is triggered (The database is called only once to update the cache).
With the cache key data, the expire (ttl) and outdate time of cache key, updated too.

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
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }

First create an adapter for your cache service (or database), by inheriting the "BaseCache" abstract class.

    public interface IMyCacheAdapter: ICacheAdapter{}
    public class MyCacheAdapter : CacheAdapter,IMyCacheAdapter{
        // Override abstract methods
    }

Then inject your adapter in ConfigureServices:

    services.AddSingleton<IMyCacheAdapter>(provider =>
        new MyCacheAdapter(
            provider.GetService<IServiceScopeFactory>(),
            TimeSpan.FromMinutes(2), // DefaultOutdatedAt
            TimeSpan.FromHours(1) //DefaultExpiredAt
        ));

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
        private readonly IMyCacheAdapter _cache;
        public CachedTodoService(IMyCacheAdapter cache) => _cache = cache;

        public override async Task<int> GetAsync() =>
            await _cache.GetOrCreateAsync<int, IToDoService>("todo_service_cache_key",
                async (toDoService, updateIsInProgress) =>
                {
                    try
                    {
                        var value = await toDoService.GetAsync();
                        return (value, true);
                    }
                    catch (Exception)
                    {
                        return (0, false);
                    }
                });
    }
