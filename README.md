# Problem and the solution

## Caching patterns

When you are caching data from a resource, there are caching patterns that you can implement, including proactive and reactive approaches. Two common approaches are cache-aside or lazy loading (a reactive approach) and write-through (a proactive approach). A cache-aside cache is updated after the data is requested. A write-through cache is updated immediately when the primary database is updated.

## Cache-Aside (Lazy Loading) Disadvantage

Cache misses often cause many requests to be referred to the resource, simultaneously until the data is cached again. It can reduce system performance and functionality.

![cache-aside](https://github.com/n-yousefi/AutoCache/blob/master/img/cache-aside.jpg)

# Why AutoCache?

With cache coalescing and using a two-level response, there are no real cache misses. It is a cache-aside approache, but in practice, it works similar to the write-through method and the cache is updated before the next request.

I am currently using this library for a heavy-load application. This program receives more than **30 million** requests per day and handles them with redis using AutoCache.

# How it works?

Each cache keys have "outdate" and "expire" times. When a key gets "outdated", the cache update starts with the first incoming request. In the meanwhile, all new requests receive outdated data and do not wait.

![cache-aside](https://github.com/n-yousefi/AutoCache/blob/master/img/autocache.jpg)

Suppose hundreds of requests arrived at the same time, looking for an outdated cache item. Instead of referring all of them to the resource, all requests will get outdated data from the cache and the resource is called only once (to update the cache).

## Coalescing

If the key is missing and there is no outdated value, a request will fire the cache update task. All other request wait for the result to be ready.

![cache-aside](https://github.com/n-yousefi/AutoCache/blob/master/img/coalescing.jpg)

# Installation

[![NuGet](https://img.shields.io/badge/AutoCache-nuget-green)](https://www.nuget.org/packages/AutoCache/)

First, [install NuGet](http://docs.nuget.org/docs/start-here/installing-nuget). Then, install [AutoCache](https://www.nuget.org/packages/AutoCache/) from the package manager console:

```
PM> Install-Package AutoCache
```

### How do I get started?

"CacheAdapter" class implements "ICacheAdapter" interface and has tree abstract methods. You must implement them to have the fourth method.

    public interface ICacheAdapter
    {
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
        public abstract Task RemoveAsync(string key);

        public Task<T> GetOrCreateAsync<T>(string key,
            Func<Task<(T, bool)>> resourceFetch,
            TimeSpan? outdatedAt = null,
            TimeSpan? expireAt = null,
            TimeSpan? timeout = null);
    }

First create your cache adapter:

    public interface IMyCacheAdapter: ICacheAdapter{}
    public class MyCacheAdapter : CacheAdapter,IMyCacheAdapter{
        // Override abstract methods
    }

Then inject your adapter in ConfigureServices:

    services.AddSingleton<IMyCacheAdapter>(provider =>
        new MyCacheAdapter(
            TimeSpan.FromMinutes(2), // DefaultOutdatedAt
            TimeSpan.FromHours(1), //DefaultExpiredAt
            TimeSpan.FromSeconds(30) //DefaultSourceFetchTimeout
        ));

Now you can use it:

    public interface IToDoService
    {
        Task<int> GetAsync();
    }

    public class ToDoService: IToDoService
    {
        public virtual async Task<int> GetAsync() {
            // read from resource (database,api, etc.), service or resource ...
            throw new NotImplementedException();
        };
    }

    public class CachedTodoService:ToDoService
    {
        private readonly IMyCacheAdapter _cache;
        public CachedTodoService(IMyCacheAdapter cache) => _cache = cache;

        public override async Task<int> GetAsync() =>
            await _cache.GetOrCreateAsync<int>("todo_service_cache_key",
                async () =>
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
