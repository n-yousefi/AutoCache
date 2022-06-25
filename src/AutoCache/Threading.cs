using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCache
{
    public class Threading
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks
            = new ConcurrentDictionary<string, SemaphoreSlim>();

        public static async Task ExecuteExclusiveTask<T>(
            string key,
            Task<T> task,
            TimeSpan waitMillisecondTimeout)
        {
            Console.Log("execute exclusive task. timeout:" + waitMillisecondTimeout);
            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(waitMillisecondTimeout))
            {
                Console.Log("execute exclusive task. doesn't wait and run the task");
                return;
            }
            try
            {
                Console.Log("execute exclusive task. run task after waiting");
                await task;
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
    }
}