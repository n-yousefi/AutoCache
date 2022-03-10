using System;
using System.Threading.Tasks;

namespace AutoCache
{
    public interface ICacheAdapter
    {
        public Task<T> GetOrCreateAsync<T, TService>(string key,
            Func<TService, bool, Task<(T, bool)>> DbFetch,
            double? outdatedAtMiliSecond = null,
            double? expireAtMiliSecond = null);

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, DateTime expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}