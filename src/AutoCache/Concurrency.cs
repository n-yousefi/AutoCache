using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("UnitTests")]
namespace AutoCache
{
    internal static class Concurrency<T>
    {
        internal sealed class Locks : ConcurrentDictionary<string, SemaphoreSlim>
        {
            private static readonly Lazy<Locks> Lazy = new Lazy<Locks>(Create);
            internal static Locks Dic => Lazy.Value;

            private Locks(ConcurrentDictionary<string, SemaphoreSlim> dictionary) : base(dictionary)
            { }
            private static Locks Create()
            {
                return new Locks(new ConcurrentDictionary<string, SemaphoreSlim>());
            }
        }

        internal static bool StartTransaction(string key, TimeSpan timeOut)
        {
            var transactionLock = Locks.Dic.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            return transactionLock.Wait(timeOut);
        }
        private static readonly Object releaseLock = new Object();
        internal static void EndTransaction(string key)
        {
            lock (releaseLock)
            {
                if (Locks.Dic.TryGetValue(key, out SemaphoreSlim removedLock))
                    removedLock.Release();
            }
        }
    }
}