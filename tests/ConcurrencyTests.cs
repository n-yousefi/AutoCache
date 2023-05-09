using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UnitTests.Mocks;
using UnitTests.Services;
using AutoCache;
using Xunit;

namespace UnitTests
{
    public class ConcurrencyTests
    {
        [Fact]
        public async Task StartTransaction_KeyIsLockedAndTimeIsOut_ReturnFalse()
        {
            // Arrange
            var return0 = await Concurrency.StartTransaction("key5", TimeSpan.FromSeconds(1));

            // Act
            var return1 = await Task.Run(async () => await Concurrency.StartTransaction("key5", TimeSpan.FromSeconds(1)));
            var return2 = await Task.Run(async () => await Concurrency.StartTransaction("key5", TimeSpan.FromSeconds(1)));
            var return3 = await Task.Run(async () => await Concurrency.StartTransaction("key5", TimeSpan.FromSeconds(1)));

            // Assert
            Assert.True(return0);
            Assert.False(return1 || return2 || return3);
        }

        [Fact]
        public async Task EndTransaction_ReleaseTheKey_ReturnTrue()
        {
            // Arrange
            Concurrency.StartTransaction("key", TimeSpan.FromSeconds(1));

            // Act
            var task = Task.Run(async() => await Concurrency.StartTransaction("key", TimeSpan.FromSeconds(10)));
            Concurrency.EndTransaction("key");
            var return1 = await task;

            // Assert
            Assert.True(return1);
        }
    }
}