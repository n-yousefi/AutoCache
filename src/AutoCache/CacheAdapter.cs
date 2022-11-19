using System;
using System.Threading.Tasks;
using AutoCache.Models;

namespace AutoCache
{
    public abstract class CacheAdapter : ICacheAdapter
    {
        private readonly TimeSpan _defaultRefreshAt;
        private readonly TimeSpan _defaultExpireAt;
        private readonly TimeSpan _defaultTimeout;

        protected CacheAdapter(TimeSpan defaultRefreshAt,
            TimeSpan defaultExpireAt,
            TimeSpan defaultTimeout)
        {
            _defaultRefreshAt = defaultRefreshAt;
            _defaultExpireAt = defaultExpireAt;
            _defaultTimeout = defaultTimeout;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<(T, bool)>> sourceFetch,
            TimeSpan? refreshAt = null,
            TimeSpan? expireAt = null,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key cannot be null or empty string.");
            var (val, hit) = await GetAsync<CacheValue<T>>(key);
            if (hit) // Two level caching
            {
                if (val.TimeToRefresh())
                    _ = UpdateCacheAsync(key, sourceFetch, refreshAt, expireAt);                
                return val.Value;
            }
            else // Coalescing
            {
                try
                {
                    if (!Concurrency<T>.StartTransaction(key, timeout ?? _defaultTimeout))
                        throw new TimeoutException("Source fetch timeout expired.");

                    (val, hit) = await GetAsync<CacheValue<T>>(key);

                    if (!hit || val.TimeToRefresh())
                        (val, hit) = await UpdateCacheAsync(key, sourceFetch, refreshAt, expireAt);
                }
                finally
                {
                    Concurrency<T>.EndTransaction(key);
                }

                if (!hit)
                    throw new TimeoutException("Cannot fetch from the source.");

                return val.Value;
            }
        }

        private async Task<(CacheValue<T>, bool)> UpdateCacheAsync<T>(string key,
            Func<Task<(T, bool)>> sourceFetch,
            TimeSpan? refreshAt = null,
            TimeSpan? expireAt = null)
        {
            var (sourceValue, hasValue) = await sourceFetch();
            if (hasValue)
            {
                var cacheValue = new CacheValue<T>
                {
                    Value = sourceValue,
                    RefreshAt = DateTime.Now.Add(refreshAt ?? _defaultRefreshAt)
                };
                try
                {
                    await SetAsync(key, cacheValue, expireAt ?? _defaultExpireAt);
                }
                catch (Exception ex)
                {
                    Console.Write(ex);
                }
                return (cacheValue, true);
            }
            await RemoveAsync(key);
            return (default, false)!;
        }

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}