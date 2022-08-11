using Microsoft.Extensions.Logging;
using System;

namespace UnitTests.Mocks
{
    public class ConsoleLogger : ILogger
    {

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"{formatter(state, exception)}");

            Console.WriteLine();
        }
    }
}
