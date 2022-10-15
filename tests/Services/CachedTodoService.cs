using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AutoCache;

namespace UnitTests.Services
{
    public class CachedTodoService : ToDoService
    {
        private readonly ICacheAdapter _cache;
        private readonly TimeSpan _readFromSourceDelay;
        private readonly TimeSpan _expireAt;
        private readonly TimeSpan _refreshAt;

        public CachedTodoService(ICacheAdapter cache,
            TimeSpan refreshAt,
            TimeSpan expireAt,
            TimeSpan readFromSourceDelay)
        {
            _cache = cache;
            _refreshAt = refreshAt;
            _expireAt = expireAt;
            _readFromSourceDelay = readFromSourceDelay;
        }

        public override async Task<int> GetAsync(string key) =>
            await _cache.GetOrCreateAsync<int>("todo_service_cache_key",
                async () =>
                {
                    try
                    {
                        //Console.Log($"{key} source fetch started");
                        await Task.Delay(_readFromSourceDelay);
                        var value = await base.GetAsync(key);
                        //Console.Log($"{key} source fetch successfull");
                        return (value, true);
                    }
                    catch (Exception)
                    {
                        return (0, false);
                    }
                }, _refreshAt,
                _expireAt);
    }
}
