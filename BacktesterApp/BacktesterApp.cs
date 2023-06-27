using Broker;
using Broker.Backtesting;
using Broker.Reports;
using Broker.Strategies;
using Broker.Utils;
using NLog;

namespace BacktesterApp
{
    internal class BacktesterApp
    {
        const string LogsPath = "C:\\tradingbot\\logs";

        static async Task Main(string[] args)
        {
            //TODO : handle args

            var latestTradingDay = new DateOnly(2023, 05, 18);

            var logger = LogManager.GetLogger($"Backtester");

            var broker = new Backtester(latestTradingDay, logger);
            broker.TimeCompression.Factor = 0.0015;
            var trader = new Trader(broker, logger);

            var marketHours = latestTradingDay.ToMarketHours();
            trader.AddStrategy(new BollingerBandsStrategy(marketHours.Item1, marketHours.Item2, "GME", trader));

            var traderTask = trader.Start();
            var backtesterTask = broker.Start();

            await Task.WhenAll(traderTask, backtesterTask);
            var results = traderTask.Result;
            var bkResults = backtesterTask.Result;

            TradingViewIndicatorGenerator.GenerateReport(Path.Combine(LogsPath, $"tvResults-{latestTradingDay.ToShortDateString()}.txt"), results);
        }
    }
}