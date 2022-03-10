using System;

namespace AutoCache.Models
{
    public class CacheValue<T>
    {
        public CacheValue(T value, DateTime outdatedAt)
        {
            OutdatedAt = outdatedAt;
            Value = value;
        }

        public DateTime OutdatedAt { get; set; }
        public T Value { get; set; } = default!;

        public bool IsOutdated() => DateTime.Now >= OutdatedAt;
    }
}
