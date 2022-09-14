using System;

namespace TradingBot.Utils
{
    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Console.WriteLine(message);
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }

    public class NoLogger : ILogger
    {
        public void LogDebug(string message) {}
        public void LogInfo(string message) {}
        public void LogWarning(string message) { }
        public void LogError(string message) { }
    }
}
