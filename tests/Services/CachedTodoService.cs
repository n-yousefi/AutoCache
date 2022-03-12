using System;
using System.Threading.Tasks;
using AutoCache;

namespace UnitTests.Services
{
    public class CachedTodoService : ToDoService
    {
        private readonly ICacheAdapter _cache;

        public CachedTodoService(ICacheAdapter cache) => _cache = cache;

        public override async Task<int> GetAsync() =>
            await _cache.GetOrCreateAsync<int, IToDoService>("todo_service_cache_key",
                async (rep, updateIsInProgress) =>
                {
                    try
                    {
                        var value = await rep.GetAsync();
                        return (value, true);
                    }
                    catch (Exception)
                    {
                        return (0, false);
                    }
                }, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
    }
}
