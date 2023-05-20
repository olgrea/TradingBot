using NLog;
using TradingBot;
using TradingBot.IBKR;
using TradingBot.Strategies.TestStrategies;
using TradingBot.Utils;

namespace TraderApp
{
    internal class TraderApp
    {
        static async Task Main(string[] args)
        {
            var logger = LogManager.GetLogger($"Trader");
            var broker = new IBBroker(1337, logger);
            var trader = new Trader(broker, logger);
            var today = DateTime.Now.ToMarketHours();
            trader.AddStrategy(new BollingerBandsStrategy(today.Item1, today.Item2, "GME", trader));
            await trader.Start();
        }
    }
}