using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;

public static class Logger
{
    public static void LogD(this ILogger logger, string message)
    {
        logger.LogDebug($"Thread:{Thread.CurrentThread.ManagedThreadId} Time:{DateTime.Now.TimeOfDay} {message}");
    }

    public static void LogI(this ILogger logger, string message)
    {
        logger.LogInformation(message);
    }

    public static void LogE(this ILogger logger, string message)
    {
        logger.LogError($"Thread:{Thread.CurrentThread.ManagedThreadId} Time:{DateTime.Now.TimeOfDay} {message}");
    }

    
}