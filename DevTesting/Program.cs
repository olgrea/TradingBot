using System;
using TradingBot;
using TradingBot.Strategies;

namespace ConsoleApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Trader trader = new Trader("GME");
            trader.AddStrategyForTicker<RSIDivergenceStrategy>();
            
            trader.Start();

            Console.ReadKey();
            trader.Stop();
        }
    }
}
