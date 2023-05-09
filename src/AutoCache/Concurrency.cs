using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("UnitTests")]
namespace AutoCache
{
    internal static class Concurrency
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Semaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        internal static async Task<bool> StartTransaction(string key, TimeSpan timeOut)
        {
            SemaphoreSlim semaphore = Semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(timeOut);
        }

        public static void EndTransaction(string key)
        {
            if (Semaphores.TryGetValue(key, out SemaphoreSlim semaphore))
            {
                semaphore.Release();
            }
        }
    }
}