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
        public async Task BeforeRefresh_ReturnOldState()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMinutes(1),
                refreshAt: TimeSpan.FromMilliseconds(100),
                expireAt: TimeSpan.FromMinutes(1),
                readFromSourceDelay: TimeSpan.FromMilliseconds(0));

            // Act
            cachedSvc.Set("key4", 1);
            var firstRequest = await cachedSvc.GetAsync("key4");

            cachedSvc.Set("key4", 2);
            var secondRequest = await cachedSvc.GetAsync("key4");

            // Assert
            Assert.Equal(firstRequest, 1);
            Assert.Equal(secondRequest, 1);
        }

        [Fact]
        public async Task AfterRefresh_ReturnNewState()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMinutes(1),
                refreshAt: TimeSpan.FromMilliseconds(1),
                expireAt: TimeSpan.FromMinutes(1),
                readFromSourceDelay: TimeSpan.FromMilliseconds(0));

            // Act
            cachedSvc.Set("key1", 1);
            var firstRequest = await cachedSvc.GetAsync("key1");

            cachedSvc.Set("key1", 2);
            await Task.Delay(TimeSpan.FromMilliseconds(1));

            // cache update triggred but old state returned
            var secondRequest = await cachedSvc.GetAsync("key1");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            var thridRequest = await cachedSvc.GetAsync("key1");

            // Assert
            Assert.Equal(firstRequest, 1);
            Assert.Equal(secondRequest, 1);
            Assert.Equal(thridRequest, 2);
        }

        [Fact]
        public async Task Coalescing_ServeralRequestsForAMissingKey_OneAccessShouldHappend()
        {
            // Arrange
            var cachedSvc = GetService(
                sourceFetchTimeout: TimeSpan.FromMinutes(1),
                refreshAt: TimeSpan.FromSeconds(30),
                expireAt: TimeSpan.FromSeconds(60),
                readFromSourceDelay: TimeSpan.FromSeconds(1));

            // Act
            cachedSvc.Set("key2", 1);

            await Task.Delay(TimeSpan.FromMilliseconds(11));
            var t1 = Task.Run(() => cachedSvc.GetAsync("key2"));
            var t2 = Task.Run(() => cachedSvc.GetAsync("key2"));
            var t3 = Task.Run(() => cachedSvc.GetAsync("key2"));
            var t4 = Task.Run(() => cachedSvc.GetAsync("key2"));

            Task.WaitAll(t1, t2, t3, t4);

            var dbAccessCount = cachedSvc.AccessCount("key2");

            // Assert
            Assert.Equal(dbAccessCount, 1);
        }
    }
}