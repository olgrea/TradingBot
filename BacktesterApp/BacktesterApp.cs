using NLog;
using TradingBotV2;
using TradingBotV2.Backtesting;

namespace BacktesterApp
{
    internal class BacktesterApp
    {
        static async Task Main(string[] args)
        {
            DateTime now = DateTime.Now.AddDays(-1);
            var logger = LogManager.GetLogger($"TraderLoggerDebug").WithProperty("now", now);
            var broker = new Backtester(DateOnly.FromDateTime(now), logger);
            broker.TimeCompression.Factor = 0.005;
            var trader = new Trader(broker);

            var traderTask = trader.Start();
            var backtesterTask = broker.Start();
            
            var results = await traderTask;
            var bkResults = await backtesterTask;

        }
    }
}