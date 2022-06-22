using System;

namespace AutoCache.Models
{
    public class CacheValue<T> 
    {
        public DateTime OutdatedAt { get; set; }
        public T Value { get; set; } = default!;

        public bool IsOutdated() => DateTime.Now >= OutdatedAt;
    }
}
