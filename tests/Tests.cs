using System;
using System.Threading.Tasks;
using AutoCache;
using UnitTests.Mocks;
using UnitTests.Services;
using Xunit;

namespace UnitTests
{
    public class Tests
    {
        public ICacheAdapter Cache { get; set; }
        public Tests()
        {
            //var mockServiceScopeFactory = ServiceScopeFactory.Get((typeof(IToDoService), new ToDoService()));
            Cache = new Cache();
        }

        [Fact]
        public async Task FirstShortCacheMissGetLongCacheValueAndTriggerCacheUpdate()
        {
            var cachedSvc = new CachedTodoService(Cache);
            Db.State = 1; // Db state changed.            
            var result = await cachedSvc.GetAsync(); // Trigger cache update.                        

            Db.State = 2; // Db state changed.
            var result2 = await cachedSvc.GetAsync(); // Cache is outdated. Cache update triggered. Until then, last cached state returned to prevent bottleneck

            await Task.Delay(10);
            var result3 = await cachedSvc.GetAsync(); // Fresh data reterned

            Assert.Equal(1, result);
            Assert.Equal(1, result2);
            Assert.Equal(2, result3);
        }

        [Fact]
        public async Task CacheCoalescing()
        {
            var cachedSvc = new CachedTodoService(Cache);
            Db.State = 1; // Db state changed.            
            var result = await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();                      
            await cachedSvc.GetAsync();  
        }
    }
}