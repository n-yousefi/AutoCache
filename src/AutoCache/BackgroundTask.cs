using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

public class BackgroundTask
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new ConcurrentDictionary<string, SemaphoreSlim>();
    public static void RunAction<T>(string key, Action<T> action,IServiceScopeFactory serviceScopeFactory)
    {
        ThreadPool.QueueUserWorkItem(state =>
        {
            var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!semaphore.Wait(0)) return;
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<T>();
                action(service);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to run the background DB action.",ex);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }
}