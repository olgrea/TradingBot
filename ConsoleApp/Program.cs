using System;
using TradingBot;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace ConsoleApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Trader trader = new Trader("GME", 1337, new ConsoleLogger());
            trader.AddStrategyForTicker<RSIDivergenceStrategy>();
            
            trader.Start();

            Console.ReadKey();
            trader.Stop();
        }
    }


    internal class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Console.WriteLine(message);
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }
}
