using NLog;
using TradingBot;
using TradingBot.IBKR;
using TradingBot.Reports;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace TraderApp
{
    internal class TraderApp
    {
        const string LogsPath = "C:\\tradingbot\\logs";

        static async Task Main(string[] args)
        {
            var logger = LogManager.GetLogger($"Trader");
            var broker = new IBBroker(1337, logger);
            var trader = new Trader(broker, logger);
            var today = DateTime.Now.ToMarketHours();
            trader.AddStrategy(new BollingerBandsStrategy(today.Item1, today.Item2, "GME", trader));
            var results = await trader.Start();

            TradingViewIndicatorGenerator.GenerateReport(Path.Combine(LogsPath, $"tvResults-{DateTime.Now.ToShortDateString()}.txt"), results);
            CSVReportGenerator.GenerateReport(Path.Combine(LogsPath, $"csvResults-{DateTime.Now.Date}.csv"), results);
        }
    }
}