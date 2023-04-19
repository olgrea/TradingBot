using NLog.Config;
using NLog;
using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;

namespace BacktesterTests
{
    internal class BacktesterOrderManagerTests : IBBrokerTests.OrderManagerTests
    {
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            ConfigurationItemFactory.Default.Targets.RegisterDefinition("NUnitLogger", typeof(TradingBotV2.Tests.NunitTargetLogger));

            var logger = LogManager.GetLogger($"{nameof(BacktesterOrderManagerTests)}", typeof(TradingBotV2.Tests.NunitTargetLogger));
            _backtester = new Backtester(new DateTime(2023, 04, 10), logger);
            _broker = _backtester;
            await _broker.ConnectAsync();

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [SetUp]
        public void SetUp()
        {
            _backtester.Reset();
            _backtester.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _backtester.Stop();
        }

        protected override bool IsMarketOpen()
        {
            return true;
        }
    }
}
