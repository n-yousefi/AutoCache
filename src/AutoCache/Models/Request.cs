using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AutoCache.Models
{
    internal class Request<T>
    {
        internal Request(Func<Task<(T, bool)>> sourceFetch, TimeSpan? refreshAt = null, TimeSpan? expireAt = null)
        {
            SourceFetch = sourceFetch;
            RefreshAt = refreshAt;
            ExpireAt = expireAt;
        }
        public Func<Task<(T, bool)>> SourceFetch { get; set; }
        public TimeSpan? RefreshAt { get; set; }
        public TimeSpan? ExpireAt { get; set; }
    }
}
