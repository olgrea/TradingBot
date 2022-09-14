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
}
