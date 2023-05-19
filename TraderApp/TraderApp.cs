using NLog;
using TradingBotV2;
using TradingBotV2.IBKR;
using TradingBotV2.Strategies.TestStrategies;
using TradingBotV2.Utils;

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