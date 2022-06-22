using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AutoCache.Models
{
    internal class Request<T>
    {
        internal Request(Func<Task<(T, bool)>> sourceFetch, TimeSpan? outdatedAt=null, TimeSpan? expireAt = null)
        {
            SourceFetch = sourceFetch;
            OutdatedAt = outdatedAt;
            ExpireAt = expireAt;
        }
        public Func<Task<(T, bool)>> SourceFetch { get; set; }
        public TimeSpan? OutdatedAt { get; set; }
        public TimeSpan? ExpireAt { get; set; }
    }
}
