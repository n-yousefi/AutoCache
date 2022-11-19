using System;

namespace AutoCache.Models
{
    public class CacheValue<T>
    {
        public DateTime RefreshAt { get; set; }
        public T Value { get; set; } = default!;

        public bool TimeToRefresh() => DateTime.Now >= RefreshAt;
    }
}
