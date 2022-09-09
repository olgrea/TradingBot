using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Strategies;
using TradingBot.Utils;
using Bar = TradingBot.Broker.MarketData.Bar;

namespace TradingBot
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


    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Console.WriteLine(message);
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }
}
