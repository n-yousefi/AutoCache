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

            await Task.Delay(TimeSpan.FromMilliseconds(110));
            var result3 = await cachedSvc.GetAsync();

            // Assert
            Assert.Equal(1, result);
            Assert.Equal(1, result2);
            Assert.Equal(2, result3);
        }

        [Fact]
        public async Task CheckOutdatedAtAndCoalescing()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMilliseconds(1000000),
                outdatedAt: TimeSpan.FromMilliseconds(10),
                expireAt: TimeSpan.FromMilliseconds(1000000),
                readFromSourceDelay: TimeSpan.FromMilliseconds(10000));

            // Act
            Db.State = 1;
            await cachedSvc.GetAsync();

            Db.State = 2;
            await Task.Delay(TimeSpan.FromMilliseconds(11));
            var t1 = cachedSvc.GetAsync();
            var t2 = cachedSvc.GetAsync();
            var t3 = cachedSvc.GetAsync();
            var t4 = cachedSvc.GetAsync();
            Task.WaitAll(t1, t2, t3, t4);


            // Assert
            Assert.Equal(2, t1.Result);
            Assert.Equal(1, t2.Result);
            Assert.Equal(1, t3.Result);
            Assert.Equal(1, t4.Result);
        }
    }
}