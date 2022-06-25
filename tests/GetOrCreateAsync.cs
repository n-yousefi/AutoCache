using System;
using System.Threading;
using System.Threading.Tasks;
using AutoCache;
using UnitTests.Mocks;
using UnitTests.Services;
using Xunit;

namespace UnitTests
{
    public class GetOrCreateAsync
    {
        private CachedTodoService GetService(
            TimeSpan sourceFetchTimeout,
            TimeSpan outdatedAt,
            TimeSpan expireAt,
            TimeSpan readFromSourceDelay)
        {
            return new CachedTodoService(new Cache(sourceFetchTimeout),
                outdatedAt,
                expireAt,
                readFromSourceDelay
            );
        }

        [Fact]
        public async Task CheckExpireAt()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMilliseconds(1000000),
                outdatedAt: TimeSpan.FromMilliseconds(100),
                expireAt: TimeSpan.FromMilliseconds(100),
                readFromSourceDelay: TimeSpan.FromMilliseconds(0));

            // Act
            Db.State = 1;       
            var result = await cachedSvc.GetAsync();                        

            Db.State = 2;
            var result2 = await cachedSvc.GetAsync(); 

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            var result3 = await cachedSvc.GetAsync(); 

            // Assert
            Assert.Equal(1, result);
            Assert.Equal(1, result2);
            Assert.Equal(2, result3);
        }

        [Fact]
        public async Task FirstShortCacheMissGetLongCacheValueAndTriggerCacheUpdate()
        {
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMilliseconds(1000000),
                outdatedAt: TimeSpan.FromMilliseconds(2000),
                expireAt: TimeSpan.FromMilliseconds(30),
                readFromSourceDelay: TimeSpan.FromMilliseconds(30));

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
            //var cachedSvc = new CachedTodoService(Cache);
            //Db.State = 1; // Db state changed.            
            //var result = await cachedSvc.GetAsync();
            //
            //var t1 = cachedSvc.GetAsync(); 
            //var t2 = cachedSvc.GetAsync();
            //var t3 = cachedSvc.GetAsync();
            //var t4 = cachedSvc.GetAsync();
            //Task.WaitAll(t1, t2, t3, t4);
        }
    }
}