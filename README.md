# AutoCache

AutoCache is a .NET library that optimizes source caching in high traffic applications. It utilizes a cache-aside approach, but in practice, it works similar to a write-through method and updates the cache before the next request. This library also includes cache coalescing which eliminates real cache misses and improves system performance.

## Problem

When caching data from a resource, there are two common approaches: cache-aside or lazy loading and write-through. Cache-aside or lazy loading is a reactive approach, where the cache is updated after the data is requested. Write-through is a proactive approach, where the cache is updated immediately when the primary database is updated.

The disadvantage of cache-aside is that cache misses often cause many requests to be referred to the resource simultaneously until the data is cached again, reducing system performance and functionality.

![cache-aside](https://raw.githubusercontent.com/n-yousefi/AutoCache/master/img/cache-aside.jpg)

## Solution

AutoCache solves this problem by adding a "refresh" time to each key. When it's time to refresh a key, the cache update starts with the first incoming request. All requests receive the response without waiting for the update. The "refresh" and "expiration" times are updated after each refresh.

![autocache](https://raw.githubusercontent.com/n-yousefi/AutoCache/master/img/autocache.jpg)

AutoCache also includes cache coalescing which means that on a cache key miss, only the first request will fire the cache update task. All other requests will wait for the result to be ready.

![coalescing](https://raw.githubusercontent.com/n-yousefi/AutoCache/master/img/coalescing.jpg)

## Usage

### Installation

[![NuGet](https://img.shields.io/badge/AutoCache-nuget-green)](https://www.nuget.org/packages/AutoCache/)

First, [install NuGet](http://docs.nuget.org/docs/start-here/installing-nuget). Then, install [AutoCache](https://www.nuget.org/packages/AutoCache/) from the package manager console:

```
PM> Install-Package AutoCache
```

### Getting Started

1. Create your cache adapter by implementing the ICacheAdapter interface:

   ```csharp
    public interface IMyCacheAdapter: ICacheAdapter{}
    public class MyCacheAdapter : CacheAdapter,IMyCacheAdapter{
        // Override abstract methods
    }
    ```
    
2. Inject your adapter in ConfigureServices:

   ```csharp
    services.AddSingleton<IMyCacheAdapter>(provider =>
        new MyCacheAdapter(
            TimeSpan.FromMinutes(2), // DefaultRefreshAt
            TimeSpan.FromHours(1), //DefaultExpiredAt
            TimeSpan.FromSeconds(30) //DefaultSourceFetchTimeout
        ));
    ```
    
3. Use it in your services:

   ```csharp
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
    ```

## Configuration
    
You can configure the default refreshAt, expireAt and timeout for the CacheAdapter and adjust them to your needs.
    
## Tips
    
* Depending on your business, by choosing a long time for expiration and a short time for refreshing, you can avoid cache misses and consecutive waits.
* This library is currently being used in a heavy-load application that receives more than 30 million requests per day and handles them with redis using AutoCache. 
    
    
## Changelog

[Learn about the latest improvements][changelog].



[changelog]: CHANGELOG.md
