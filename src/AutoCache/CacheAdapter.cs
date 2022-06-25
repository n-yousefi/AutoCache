using System;
using System.Threading.Tasks;
using AutoCache.Models;
using System.Collections.Concurrent;
using System.Threading;

namespace AutoCache
{
    public abstract class CacheAdapter : ICacheAdapter
    {
        private readonly TimeSpan _defaultOutdatedAt;
        private readonly TimeSpan _defaultExpireAt;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public CacheAdapter(TimeSpan defaultOutdatedAt,
            TimeSpan defaultExpireAt)
        {
            _defaultOutdatedAt = defaultOutdatedAt;
            _defaultExpireAt = defaultExpireAt;
        }
        public async Task<T> GetOrCreateAsync<T, TService>(
            string key,
            Func<Task<(T, bool)>> sourceFetch,
            TimeSpan? outdatedAt = null,
            TimeSpan? expireAt = null)
        {
            var request = GetRequestParamsObject(sourceFetch, outdatedAt, expireAt);
            if (!string.IsNullOrEmpty(key))
            {
                // Fetch from cache
                var (cacheValue, cacheHit) = await GetAsync<CacheValue<T>>(key);

                // Cache hit and is not outdated
                if (cacheHit && !cacheValue.IsOutdated())
                    return cacheValue.Value;

                // Cache update 
                var task = GetFromCacheOrUpdateTheCache(key, request);
                var waitMillisecondTimeout = cacheHit ? 0 : 30000;
                (cacheValue, cacheHit) = await ExecuteExclusiveTask(key, task, waitMillisecondTimeout);

                if (cacheHit)
                    return cacheValue.Value;
            }
            throw new Exception("Cache update timeout expired.");
        }

        private Request<T> GetRequestParamsObject<T>(Func<Task<(T, bool)>> sourceFetch, TimeSpan? outdatedAt, TimeSpan? expireAt)
        {
            return new Request<T>(sourceFetch, outdatedAt, expireAt);
        }

        
        private async Task<(CacheValue<T>, bool)> ExclusiveCacheUpdateQQQQQQQQQQQ<T>(
            string key,
            Task<(CacheValue<T>,bool)> task,
            bool shouldWait)
        {
            bool cacheHit = default;
            CacheValue<T> cacheValue = default!;
            // Wait for it on cache miss and don't wait when there is an outdated cache
            var millisecondTimeout = shouldWait ? 30000 : 0;

            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (await semaphore.WaitAsync(millisecondTimeout))
            {
                try
                {
                    (cacheValue, cacheHit) = await task;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to run the background action.", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            return (cacheValue, cacheHit);
        }

        private async Task<T> ExecuteExclusiveTask<T>(
            string key,
            Task<T> task,
            int waitMillisecondTimeout)
        {
            T result = default;

            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (await semaphore.WaitAsync(waitMillisecondTimeout))
            {
                try
                {
                    result = await task;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to run the background action.", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            return result;
        }



        private async Task<(CacheValue<T>, bool)> GetFromCacheOrUpdateTheCache<T>(string key,
            Request<T> request)
        {
            // Fetch from cache
            var (cacheValue, cacheHit) = await GetAsync<CacheValue<T>>(key);

            // Update cache and get new value
            if (!cacheHit || cacheValue.IsOutdated())
                (cacheValue, cacheHit) = await UpdateTheCache(key, request);
            return (cacheValue, cacheHit);
        }

        private async Task<(CacheValue<T>, bool)> UpdateTheCache<T>(string key,
            Request<T> request)
        {
            // Get from source
            var (sourceValue, hasValue) = await request.SourceFetch();
            if (hasValue)
            {
                var cacheValue = ConvertSourceValueToCacheValue(sourceValue, request.OutdatedAt);                
                await SetAsync(key, cacheValue, request.ExpireAt ?? _defaultExpireAt);
                return (cacheValue, true);
            }
            await RemoveAsync(key);
            return (default, false)!;
        }

        private CacheValue<T> ConvertSourceValueToCacheValue<T>(T sourceValue, TimeSpan? outdatedAt)
        {
            return new CacheValue<T>()
            {
                Value = sourceValue,
                OutdatedAt = DateTime.Now.Add(outdatedAt ?? _defaultOutdatedAt)
            };
        }

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}