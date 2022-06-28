# Why AutoCache?

Cache misses often cause many requests to be referred to the database, service, or resource simultaneously until the data is cached again. It can reduce system performance and functionality.
With cache coalescing and using a two-level response, there are no real cache misses.

# How it works?

Each cache keys have "outdate" and "expire" times. When a key gets "outdated", the cache update starts with the first incoming request. In the meanwhile, all new requests receive outdated data and do not wait.

Suppose hundreds of requests arrived at the same time, looking for an outdated cache item. Instead of referring all of them to the database, all requests will get outdated data from the cache and the database is called only once (to update the cache).

## Coalescing

If the key is missing and there is no outdated value, a request will fire the cache update task. All other request wait for the result to be ready.

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
            Func<Task<(T, bool)>> dbFetch,
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
            // read from DB, service or resource ...
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
