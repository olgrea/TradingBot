using NLog.Config;
using NLog;
using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;
using TradingBotV2.Broker.MarketData;
using NLog.TradingBot;

namespace BacktesterTests
{
    internal class BacktesterOrderManagerTests : IBBrokerTests.OrderManagerTests
    {
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            var logger = LogManager.GetLogger($"NUnitLogger", typeof(NunitTargetLogger));

            // The 10:55:00 here is just so the order gets filled rapidly in test AwaitExecution_OrderGetsFilled_Returns ...
            DateTime dateTime = new DateTime(2023, 04, 10);
            _backtester = new Backtester(dateTime.Add(new TimeSpan(10, 55, 00)), dateTime.Add(MarketDataUtils.MarketEndTime), logger);
            _broker = _backtester;
            _backtester.DbPath = TestDbPath;
            await _broker.ConnectAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            await _backtester.Reset();
            _ = _backtester.Start();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _backtester.Stop();
        }

        protected override bool IsMarketOpen()
        {
            return true;
        }
    }
}
