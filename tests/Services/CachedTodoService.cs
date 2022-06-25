using System;
using System.Threading.Tasks;
using AutoCache;

namespace UnitTests.Services
{
    public class CachedTodoService : ToDoService
    {
        private readonly ICacheAdapter _cache;
        private readonly TimeSpan _readFromSourceDelay;
        private readonly TimeSpan _expireAt;
        private readonly TimeSpan _outdatedAt;

        public CachedTodoService(ICacheAdapter cache,
            TimeSpan outdatedAt,
            TimeSpan expireAt,
            TimeSpan readFromSourceDelay)
        {
            _cache = cache;
            _outdatedAt = outdatedAt;
            _expireAt = expireAt;
            _readFromSourceDelay = readFromSourceDelay;
        }

        public override async Task<int> GetAsync() =>
            await _cache.GetOrCreateAsync<int, IToDoService>("todo_service_cache_key",
                async () =>
                {
                    try
                    {
                        await Task.Delay(_readFromSourceDelay);
                        var value = await base.GetAsync();
                        return (value, true);
                    }
                    catch (Exception)
                    {
                        return (0, false);
                    }
                }, _outdatedAt,
                _expireAt);
    }
}
