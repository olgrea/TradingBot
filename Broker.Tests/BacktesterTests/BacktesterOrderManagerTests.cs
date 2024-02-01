using Broker.Backtesting;
using Broker.Utils;
using NUnit.Framework;
using Broker.Tests;
using Broker.IBKR.Providers;

namespace BacktesterTests
{
    internal class BacktesterOrderManagerTests : IBBrokerTests.OrderManagerTests
    {
        static DateRange _dateRange= new DateTime(2024, 01, 10).ToMarketHours();
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            _backtester = TestsUtils.CreateBacktester(_dateRange.From, _dateRange.To);
            _backtester.TimeCompression.Factor = 0.002;
            _broker = _backtester;

            await Task.CompletedTask;
        }

        [SetUp]
        public override async Task SetUp()
        {
            await _broker.ConnectAsync();
            _backtester.Reset();
            _ = _backtester.Start();
        }

        [TearDown]
        public override async Task TearDown()
        {
            Assert.IsTrue((_backtester.HistoricalDataProvider as IBHistoricalDataProvider)._lastOperationStats.NbRetrievedFromIBKR == 0);
            await base.TearDown();
        }

        public static IEnumerable<(string, DateRange)> GetRequiredTestData()
        {
            yield return (TickerGME, _dateRange);
            yield return (TickerAMC, _dateRange);
        }
    }
}
