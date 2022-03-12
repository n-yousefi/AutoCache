using System;
using System.Threading.Tasks;

namespace AutoCache
{
    public interface ICacheAdapter
    {
        public Task<T> GetOrCreateAsync<T, TService>(string key,
            Func<TService, bool, Task<(T, bool)>> DbFetch,
            TimeSpan? outdatedAt = null,
            TimeSpan? expireAt = null);

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}