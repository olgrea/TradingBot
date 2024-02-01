using NUnit.Framework;
using Broker.Tests;
using Broker.Utils;
using Broker.IBKR.Providers;
using Broker.IBKR.Backtesting;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            TestsUtils.RestoreTestDb();
            _backtester = TestsUtils.CreateBacktester(new DateOnly(2024, 01, 10));
            _broker = _backtester;
            await _broker.ConnectAsync();
        }

        [SetUp]
        public void SetUp()
        {
            _backtester.Reset();
            _ = _backtester.Start();
        }

        [TearDown]
        public void TearDown()
        {
            Assert.IsTrue((_backtester.HistoricalDataProvider as IBHistoricalDataProvider)._lastOperationStats.NbRetrievedFromIBKR == 0);
            _backtester.Stop();
        }

        public static IEnumerable<(string, DateRange)> GetRequiredTestData()
        {
            var range = new DateOnly(2024, 01, 10).ToMarketHours();
            yield return ("SPY", range);
            yield return ("QQQ", range);
        }
    }
}
