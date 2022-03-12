using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AutoCache.Models;

namespace AutoCache
{
    public abstract class CacheAdapter : ICacheAdapter
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private TimeSpan _defaulOutdatedAt, _defaultExpireAt;
        public CacheAdapter(IServiceScopeFactory serviceScopeFactory,
            TimeSpan defaulOutdatedAt,
            TimeSpan defaultExpireAt)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _defaulOutdatedAt = defaulOutdatedAt;
            _defaultExpireAt = defaultExpireAt;
        }
        public async Task<T> GetOrCreateAsync<T, TService>(
            string key,
            Func<TService, bool, Task<(T, bool)>> DbFetch,
            TimeSpan? outdatedAt = null,
            TimeSpan? expireAt = null)
        {
            // DB fetch action
            async void UpdateTask(TService svc)
            {
                // Get From DB
                var (dbValue, hasValue) = await DbFetch(svc, true);
                if (hasValue)
                {
                    // Store In Cache;                    
                    var cacheValue = new CacheValue<T>()
                    {
                        Value = dbValue,
                        OutdatedAt = DateTime.Now.Add(outdatedAt ?? _defaulOutdatedAt)
                    };
                    await SetAsync(key, cacheValue, expireAt ?? _defaultExpireAt);
                }
                else
                {
                    await RemoveAsync(key);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(key))
            {
                // Fetch from cache
                var (cacheValue, hasValue) = await GetAsync<CacheValue<T>>(key);

                // Cache hit and is not outdated
                if (hasValue && !cacheValue.IsOutdated())
                    return cacheValue.Value;

                // Trigger cache update on cache miss or outdated
                BackgroundTask.RunAction<TService>(key, UpdateTask, _serviceScopeFactory);

                // Return outdated value while caching if has
                if (hasValue)
                    return cacheValue.Value;
            }

            // Fetch from DB
            using var scope = _serviceScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TService>();
            var (dbVal, hasVal) = await DbFetch(service, false).ConfigureAwait(false);
            return dbVal;
        }

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}