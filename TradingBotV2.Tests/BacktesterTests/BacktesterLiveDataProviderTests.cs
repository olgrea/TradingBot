using NLog;
using NLog.TradingBot;
using NUnit.Framework;
using TradingBotV2.Backtesting;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;
        Task _backtestingTask;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            var logger = LogManager.GetLogger($"{nameof(BacktesterLiveDataProviderTests)}", typeof(NunitTargetLogger));
            _backtester = new Backtester(new DateTime(2023, 04, 03), logger);
            _broker = _backtester;
            await _broker.ConnectAsync();

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [SetUp]
        public async Task SetUp()
        {
            // TODO : Is there a better way?
            await _backtester.Reset();
            _backtestingTask = _backtester.Start();
            _ = _backtestingTask.ContinueWith(t =>
            {
                var e = t.Exception ?? new Exception("Backtesting task faulted.");
                _tcs?.TrySetException(e);
            }, TaskContinuationOptions.OnlyOnFaulted);
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
