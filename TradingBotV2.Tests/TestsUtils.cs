using NLog;
using NLog.TradingBot;
using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace TradingBotV2.Tests
{
    internal static class TestsUtils
    {
        public const string TestDbPath = @"C:\tradingbot\db\tests.sqlite3";
        
        public static void PrintCurrentTestName(this ILogger logger)
        {
            logger?.Info($"=== CURRENT TEST : {TestContext.CurrentContext.Test.Name}");
        }

        public static ILogger CreateLogger()
        {
            return LogManager.GetLogger($"NUnitLogger", typeof(NunitTargetLogger));
        }

        public static IBBroker CreateBroker(ILogger? logger = null)
        {
            var broker = new IBBroker(9001, logger);
            var historicalProvider = (IBHistoricalDataProvider)broker.HistoricalDataProvider;
            historicalProvider.DbPath = TestDbPath;

            //historical data provider always has a logger
            historicalProvider.Logger = logger ?? CreateLogger();
            return broker;
        }

        public static Backtester CreateBacktester(DateOnly date)
        {
            var backtester = new Backtester(date);
            backtester.DbPath = TestDbPath;
            backtester.Logger = CreateLogger();

            return backtester;
        }

        public static Backtester CreateBacktester(DateTime from, DateTime to)
        {
            var backtester = new Backtester(from, to);
            backtester.DbPath = TestDbPath;
            backtester.Logger = CreateLogger();
            return backtester;
        }

        public static DateOnly FindLastOpenDay()
        {
            var openDay = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
            while (!MarketDataUtils.WasMarketOpen(openDay))
                openDay = openDay.AddDays(-1);
            return openDay;
        }

        public static bool IsMarketOpen()
        {
            if (TestContext.CurrentContext.Test.FullName.Contains("BacktesterTests")) return true;
            else return MarketDataUtils.IsMarketOpen();
        }

        public static class Assert
        {
            public static void MarketIsOpen()
            {
                if (!IsMarketOpen())
                    NUnit.Framework.Assert.Inconclusive("Market is not open.");
            }
        }
    }
}
