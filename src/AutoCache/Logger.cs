using System;
using System.Diagnostics;
using System.Threading;

public class Console
{
    public static void Log(string message)
    {
        Debug.WriteLine($"Thread:{Thread.CurrentThread.ManagedThreadId} Time:{DateTime.Now.TimeOfDay} {message}");
    }
}