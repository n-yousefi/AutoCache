using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoCache;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests.Mocks
{
    public class CacheItem<T>
    {
        public T Value { get; set; }
        public DateTime Date { get; set; }
    }

    public class Cache : CacheAdapter
    {
        public Cache(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
        {

        }

        private readonly Dictionary<string, dynamic> _cache = new Dictionary<string, dynamic>();

        public override Task RemoveAsync(string key) => Task.FromResult(_cache.Remove(key));

        public override Task SetAsync<T>(string key, T value, DateTime expireAt)
        {
            _cache[key] = new CacheItem<T> { Value = value, Date = expireAt };
            return Task.CompletedTask;
        }

        public override Task<(T, bool)> GetAsync<T>(string key)
        {
            if (_cache.TryGetValue(key, out dynamic value))
            {
                var item = (CacheItem<T>)value;
                if (item.Date >= DateTime.Now)
                    return Task.FromResult((item.Value, true));
            }
            return Task.FromResult((default(T), false));
        }
    }
}
