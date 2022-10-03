using System;
using System.Threading.Tasks;
using TradingBot;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace ConsoleApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //var start = new DateTime(DateTime.Today.Ticks + DateTimeUtils.MarketStartTime.Ticks, DateTimeKind.Local);
            //var end = new DateTime(DateTime.Today.Ticks + DateTimeUtils.MarketEndTime.Ticks, DateTimeKind.Local);
            
            var start = new DateTime(DateTime.Today.Ticks + DateTime.Now.AddSeconds(10).TimeOfDay.Ticks, DateTimeKind.Local);
            var end = new DateTime(DateTime.Today.Ticks + DateTime.Now.AddSeconds(30).TimeOfDay.Ticks, DateTimeKind.Local);

            Trader trader = new Trader("GME", start, end, 1337);
            trader.AddStrategyForTicker<TestStrategy>();
            
            await trader.Start();
            trader.Stop();
        }
    }
}
