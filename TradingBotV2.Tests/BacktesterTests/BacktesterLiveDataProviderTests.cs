using NLog;
using NLog.Config;
using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.IBKR;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;
        Task _backtestingTask;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            // TODO : need to move that elsewhere, or create an assembly
            ConfigurationItemFactory.Default.Targets.RegisterDefinition("NUnitLogger", typeof(TradingBotV2.Tests.NunitTargetLogger));

            var logger = LogManager.GetLogger($"{nameof(BacktesterLiveDataProviderTests)}", typeof(TradingBotV2.Tests.NunitTargetLogger));
            _backtester = new Backtester(new DateTime(2023, 04, 03), logger);
            _broker = _backtester;
            await _broker.ConnectAsync();

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [SetUp]
        public void SetUp()
        {
            // TODO : Is there a better way?
            _backtester.Reset();
            _backtestingTask = _backtester.Start();
            _backtestingTask.ContinueWith(t => _tcs?.TrySetException(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
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
