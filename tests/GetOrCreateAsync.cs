using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UnitTests.Mocks;
using UnitTests.Services;
using Xunit;

namespace UnitTests
{
    public class GetOrCreateAsync
    {
        private CachedTodoService GetService(
            TimeSpan sourceFetchTimeout,
            TimeSpan refreshAt,
            TimeSpan expireAt,
            TimeSpan readFromSourceDelay)
        {
            return new CachedTodoService(new Cache(sourceFetchTimeout),
                refreshAt,
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
                refreshAt: TimeSpan.FromMilliseconds(100),
                expireAt: TimeSpan.FromMilliseconds(100),
                readFromSourceDelay: TimeSpan.FromMilliseconds(0));

            // Act
            Db.State = 1;
            var r1 = await cachedSvc.GetAsync("r1");

            Db.State = 2;
            var r2 = await cachedSvc.GetAsync("r2");

            await Task.Delay(TimeSpan.FromMilliseconds(110));
            var r3 = await cachedSvc.GetAsync("r3");

            // Assert
            Assert.Equal(1, r1);
            Assert.Equal(1, r2);
            Assert.Equal(2, r3);
        }

        [Fact]
        public async Task CheckRefreshAt()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMilliseconds(1000000),
                refreshAt: TimeSpan.FromMilliseconds(1),
                expireAt: TimeSpan.FromMilliseconds(1000000),
                readFromSourceDelay: TimeSpan.FromMilliseconds(1000));

            // Act
            Db.State = 1;
            await cachedSvc.GetAsync("r1");

            Db.State = 2;
            await Task.Delay(TimeSpan.FromMilliseconds(11));
            var t1 = Task.Run(() => cachedSvc.GetAsync("t1"));
            var t2 = Task.Run(() => cachedSvc.GetAsync("t2"));
            var t3 = Task.Run(() => cachedSvc.GetAsync("t3"));
            var t4 = Task.Run(() => cachedSvc.GetAsync("t4"));

            await Task.Delay(TimeSpan.FromMilliseconds(10000));

            Task.WaitAll(t1, t2, t3, t4);

            var t7 = cachedSvc.GetAsync("t7");

            // Assert
            var results = new List<int> { t1.Result, t2.Result, t3.Result, t4.Result, t7.Result };
            results.Where(q => q == 1).Should().HaveCount(3);
            results.Where(q => q == 2).Should().HaveCount(2);
        }

        [Fact]
        public async Task Coalescing()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMilliseconds(30000),
                refreshAt: TimeSpan.FromMilliseconds(30000),
                expireAt: TimeSpan.FromMilliseconds(60000),
                readFromSourceDelay: TimeSpan.FromMilliseconds(1000));

            // Act
            Db.State = 1;
            await Task.Delay(TimeSpan.FromMilliseconds(11));
            var t1 = Task.Run(() => cachedSvc.GetAsync("t1"));
            var t2 = Task.Run(() => cachedSvc.GetAsync("t2"));
            var t3 = Task.Run(() => cachedSvc.GetAsync("t3"));
            var t4 = Task.Run(() => cachedSvc.GetAsync("t4"));

            await Task.Delay(TimeSpan.FromMilliseconds(10000));

            // Assert
            var results = new List<int> { t1.Result, t2.Result, t3.Result, t4.Result };
            results.Where(q => q == 1).Should().HaveCount(4);
        }
    }
}