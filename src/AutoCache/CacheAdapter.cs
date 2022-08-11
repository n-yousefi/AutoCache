using System;
using System.Threading.Tasks;
using AutoCache.Models;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace AutoCache
{
    public abstract class CacheAdapter : ICacheAdapter
    {
        private readonly TimeSpan _defaultOutdatedAt;
        private readonly TimeSpan _defaultExpireAt;
        private readonly TimeSpan _defaultTimeout;
        private readonly ILogger _logger;

        protected CacheAdapter(TimeSpan defaultOutdatedAt,
            TimeSpan defaultExpireAt, TimeSpan defaultTimeout, ILogger logger)
        {
            _defaultOutdatedAt = defaultOutdatedAt;
            _defaultExpireAt = defaultExpireAt;
            _defaultTimeout = defaultTimeout;
            _logger = logger;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<(T, bool)>> sourceFetch,
            TimeSpan? outdatedAt = null,
            TimeSpan? expireAt = null,
            TimeSpan? timeout = null)
        {
            var request = GetRequestParamsObject(sourceFetch, outdatedAt, expireAt);
            if (!string.IsNullOrEmpty(key))
            {
                _logger.LogD($"start caching {key}");
                // Fetch from cache
                var (cacheValue, cacheHit) = await GetAsync<CacheValue<T>>(key);

                // Cache hit and is not outdated
                if (cacheHit && !cacheValue.IsOutdated())
                {
                    _logger.LogD($"cache hit and returned {key}");
                    return cacheValue.Value;
                }

                // Cache update 
                var sourceFetchTimeout = cacheHit
                    ? TimeSpan.Zero
                    : timeout ?? _defaultTimeout;

                // When timeout happens, it returns false to use outdated data
                var (updatedCacheValue, updatedCacheHit) = await ExecuteExclusiveTask(key, request, sourceFetchTimeout);
                
                if (updatedCacheHit) 
                    (cacheValue, cacheHit) = (updatedCacheValue, updatedCacheHit);

                _logger.LogD((cacheHit ? "succeed" : "failed") + " with result " + cacheValue.Value);

                if (cacheHit)
                    return cacheValue.Value;
            }
            throw new Exception("Cache update timeout expired.");
        }

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks
            = new ConcurrentDictionary<string, SemaphoreSlim>();

        private async Task<(CacheValue<T>?, bool)> ExecuteExclusiveTask<T>(
            string key,
            Request<T> request,
            TimeSpan waitMillisecondTimeout)
        {
            CacheValue<T>? cacheValue = default;
            bool cacheHit = default;

            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(waitMillisecondTimeout))
            {
                _logger.LogD(key + " don't wait for SEMAPHORE and use outdated data");
                return (cacheValue, cacheHit);
            }
            try
            {
                _logger.LogD(key + " SEMAPHORE locked and task started; timeout:" + waitMillisecondTimeout);
                (cacheValue, cacheHit) = await GetFromCacheOrUpdateTheCache(key, request);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to run the background action.", ex);
            }
            finally
            {
                semaphore.Release();
                _logger.LogD(key + " SEMAPHORE released");
            }
            return (cacheValue, cacheHit);
        }

        private Request<T> GetRequestParamsObject<T>(Func<Task<(T, bool)>> sourceFetch, TimeSpan? outdatedAt, TimeSpan? expireAt)
        {
            return new Request<T>(sourceFetch, outdatedAt, expireAt);
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
            var (sourceValue, hasValue) = await GetFromSource(request);
            if (hasValue)
            {
                var cacheValue = ConvertSourceValueToCacheValue(sourceValue, request.OutdatedAt);
                await SetTheCache(key, cacheValue, request.ExpireAt ?? _defaultExpireAt);
                return (cacheValue, true);
            }
            await RemoveAsync(key);
            return (default, false)!;
        }

        private async Task<(T,bool)> GetFromSource<T>(Request<T> request)
        {
            try
            {
                return await request.SourceFetch();
            }
            catch (Exception ex)
            {                 
                _logger.LogE("Cannot fetch from source. " + ex.Message);                
            }
            return (default, false);
        }

        private async Task SetTheCache<T>(string key, CacheValue<T> cacheValue, TimeSpan timeout)
        {
            try
            {
                await SetAsync(key, cacheValue, timeout);
            }
            catch (Exception ex)
            {
                _logger.LogE("Cannot set in db. " + ex.Message);
            }
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