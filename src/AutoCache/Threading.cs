using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCache
{
    public class Threading
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks
            = new ConcurrentDictionary<string, SemaphoreSlim>();

        public static async Task<T> ExecuteExclusiveTask<T>(
            string key,
            Task<T> task,
            TimeSpan waitMillisecondTimeout)
        {
            T result = default;

            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (await semaphore.WaitAsync(waitMillisecondTimeout))
            {
                try
                {
                    result = await task;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to run the background action.", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return result;
        }
    }
}