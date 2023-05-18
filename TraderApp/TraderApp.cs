using NLog;
using TradingBotV2;
using TradingBotV2.IBKR;

namespace TraderApp
{
    internal class TraderApp
    {
        static async Task Main(string[] args)
        {
            var logger = LogManager.GetLogger($"Trader");
            var broker = new IBBroker(1337, logger);
            var trader = new Trader(broker, logger);
            await trader.Start();
        }
    }
}