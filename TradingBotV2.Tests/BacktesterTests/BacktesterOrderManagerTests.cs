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
            ConfigurationItemFactory.Default.Targets.RegisterDefinition("NUnitLogger", typeof(NunitTargetLogger));

            var logger = LogManager.GetLogger($"{nameof(BacktesterOrderManagerTests)}", typeof(NunitTargetLogger));

            // The 10:55:00 here is just so the order gets filled rapidly in test AwaitExecution_OrderGetsFilled_Returns ...
            _backtester = new Backtester(new DateTime(2023, 04, 10), new TimeSpan(10, 55, 00), MarketDataUtils.MarketEndTime, logger);
            _broker = _backtester;
            await _broker.ConnectAsync();

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
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
