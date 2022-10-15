using System;
using System.Threading.Tasks;

namespace AutoCache
{
    public interface ICacheAdapter
    {
        public Task<T> GetOrCreateAsync<T>(string key,
            Func<Task<(T, bool)>> dbFetch,
            TimeSpan? refreshAt = null,
            TimeSpan? expireAt = null,
            TimeSpan? timeout = null);

        public abstract Task RemoveAsync(string key);
        public abstract Task SetAsync<T>(string key, T value, TimeSpan expireAt);
        public abstract Task<(T, bool)> GetAsync<T>(string key);
    }
}