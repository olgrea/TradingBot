using NLog;
using TradingBotV2;
using TradingBotV2.Backtesting;
using TradingBotV2.Strategies.TestStrategies;
using TradingBotV2.Utils;

namespace BacktesterApp
{
    internal class BacktesterApp
    {
        const string LogsPath = "D:\\tradingbot\\logs";

        static async Task Main(string[] args)
        {
            //TODO : handle args

            //var latestTradingDay = MarketDataUtils.FindLastOpenDay(DateTime.Now.AddDays(-1));
            var latestTradingDay = new DateOnly(2023, 05, 18);

            var logger = LogManager.GetLogger($"Backtester");
            
            var broker = new Backtester(latestTradingDay, logger);
            broker.TimeCompression.Factor = 0.0025;
            var trader = new Trader(broker);

            var marketHours = latestTradingDay.ToMarketHours();
            trader.AddStrategy(new BollingerBandsStrategy(marketHours.Item1, marketHours.Item2, "GME", trader));

            var traderTask = trader.Start();
            var backtesterTask = broker.Start();

            await Task.WhenAll(traderTask, backtesterTask);
            var results = traderTask.Result;
            var bkResults = backtesterTask.Result;
        }
    }
}