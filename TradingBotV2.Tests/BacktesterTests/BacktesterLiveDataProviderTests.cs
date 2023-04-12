using System.Diagnostics;
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
            _backtester = new Backtester(new DateTime(2023, 04, 03));
            _broker = _backtester;
            await _broker.ConnectAsync();

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [SetUp]
        public void SetUp()
        {
            _backtestingTask = _backtester.Start();
            _backtestingTask.ContinueWith(t =>
            {
                if(t.IsFaulted)
                    _tcs?.TrySetException(t.Exception);
            });
        }

        [TearDown]
        public void TearDown()
        {
            _backtester.Reset();
        }

        protected override bool IsMarketOpen()
        {
            return true;
        }
    }
}
